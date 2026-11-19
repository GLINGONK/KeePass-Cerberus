/*
  KeePass Cerberus - cascade encryption for KDBX 4.
  Licensed under the GNU GPL v2 or later (same as KeePass).

  Design
  ------
  A cascade is 1 to 4 layers among AES-256, ChaCha20, Serpent-256,
  Twofish-256 and Camellia-256, in any order, no layer used twice.
  The layer list is encoded in the KDBX data-cipher UUID itself:

      bytes  0..11 : ASCII "CerbCascade1"
      bytes 12..15 : layer ids (1..5), 0 = unused (zeros only at the end)

  so the file header fully describes the cascade and no format change
  is needed. KDBX 4 already authenticates the ciphertext (HMAC-SHA-256
  block stream + header HMAC), so each layer is a pure stream cipher
  (CTR mode for block ciphers, RFC 7539 ChaCha20).

  Every layer gets an independent key and nonce:
      key_i   = HMAC-SHA-512(K, "KPC/cascade/v1/key"   || uuid || i)[0..32]
      nonce_i = HMAC-SHA-512(K, "KPC/cascade/v1/nonce" || uuid || i || IV)[0..n]
  where K is the 32-byte KDBX encryption key and IV the 16-byte random
  header IV (fresh on every save).
*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

using KeePassLib;
using KeePassLib.Cryptography.Cipher;
using KeePassLib.Utility;

using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Parameters;

namespace KeePass.Cerberus
{
	public enum CascadeLayer : byte
	{
		None = 0,
		Aes = 1,
		ChaCha20 = 2,
		Serpent = 3,
		Twofish = 4,
		Camellia = 5
	}

	public sealed class CascadeCipherEngine : ICipherEngine2
	{
		private static readonly byte[] Magic = Encoding.ASCII.GetBytes("CerbCascade1");
		public const int MaxLayers = 4;

		private readonly CascadeLayer[] m_layers;
		private readonly PwUuid m_uuid;

		private readonly string m_strLabel = null;

		public CascadeCipherEngine(string strLabel, params CascadeLayer[] layers) : this(layers)
		{
			m_strLabel = strLabel;
		}

		public CascadeCipherEngine(params CascadeLayer[] layers)
		{
			if(!IsValidLayout(layers)) throw new ArgumentException("layers");
			m_layers = (CascadeLayer[])layers.Clone();

			byte[] pb = new byte[16];
			Array.Copy(Magic, pb, 12);
			for(int i = 0; i < m_layers.Length; ++i) pb[12 + i] = (byte)m_layers[i];
			m_uuid = new PwUuid(pb);
		}

		public CascadeLayer[] Layers { get { return (CascadeLayer[])m_layers.Clone(); } }

		public PwUuid CipherUuid { get { return m_uuid; } }

		public string DisplayName
		{
			get
			{
				List<string> l = new List<string>();
				foreach(CascadeLayer c in m_layers) l.Add(LayerName(c));
				string s = string.Join(" → ", l.ToArray());
				if(!string.IsNullOrEmpty(m_strLabel)) return m_strLabel + " : " + s;
				return ((m_layers.Length == 1) ? s : ("Cascade " + s)) + " (Cerberus)";
			}
		}

		public int KeyLength { get { return 32; } }
		public int IVLength { get { return 16; } }

		public static string LayerName(CascadeLayer c)
		{
			switch(c)
			{
				case CascadeLayer.Aes: return "AES-256";
				case CascadeLayer.ChaCha20: return "ChaCha20";
				case CascadeLayer.Serpent: return "Serpent-256";
				case CascadeLayer.Twofish: return "Twofish-256";
				case CascadeLayer.Camellia: return "Camellia-256";
				default: return "?";
			}
		}

		public static bool IsValidLayout(CascadeLayer[] layers)
		{
			if((layers == null) || (layers.Length < 1) || (layers.Length > MaxLayers))
				return false;
			bool[] seen = new bool[6];
			foreach(CascadeLayer c in layers)
			{
				int v = (int)c;
				if((v < 1) || (v > 5) || seen[v]) return false;
				seen[v] = true;
			}
			return true;
		}

		/// <summary>Decode a Cerberus cascade UUID; returns null if it is not one.</summary>
		public static CascadeCipherEngine FromUuid(PwUuid pu)
		{
			if(pu == null) return null;
			byte[] pb = pu.UuidBytes;
			for(int i = 0; i < 12; ++i) { if(pb[i] != Magic[i]) return null; }

			List<CascadeLayer> l = new List<CascadeLayer>();
			bool bEnd = false;
			for(int i = 12; i < 16; ++i)
			{
				if(pb[i] == 0) { bEnd = true; continue; }
				if(bEnd) return null; // gap -> not canonical
				l.Add((CascadeLayer)pb[i]);
			}
			CascadeLayer[] v = l.ToArray();
			return (IsValidLayout(v) ? new CascadeCipherEngine(v) : null);
		}

		public Stream EncryptStream(Stream s, byte[] pbKey, byte[] pbIV)
		{
			return Build(s, pbKey, pbIV, true);
		}

		public Stream DecryptStream(Stream s, byte[] pbKey, byte[] pbIV)
		{
			return Build(s, pbKey, pbIV, false);
		}

		private Stream Build(Stream s, byte[] pbKey, byte[] pbIV, bool bWrite)
		{
			if(s == null) throw new ArgumentNullException("s");
			if((pbKey == null) || (pbKey.Length != 32)) throw new ArgumentException("pbKey");
			if((pbIV == null) || (pbIV.Length != 16)) throw new ArgumentException("pbIV");

			// Plaintext passes layer 0 first, so layer 0 is the outermost stream
			// and the last layer sits directly on the file stream. CTR/ChaCha20
			// are symmetric, so the same chain decrypts when reading.
			Stream sCur = s;
			for(int i = m_layers.Length - 1; i >= 0; --i)
				sCur = new KeystreamXorStream(sCur, CreateKeystream(i, pbKey, pbIV), bWrite);
			return sCur;
		}

		private byte[] Derive(byte[] pbKey, string strLabel, int iLayer, byte[] pbExtra, int cb)
		{
			using(MemoryStream ms = new MemoryStream())
			{
				byte[] pbLabel = Encoding.ASCII.GetBytes(strLabel);
				ms.Write(pbLabel, 0, pbLabel.Length);
				byte[] pbU = m_uuid.UuidBytes;
				ms.Write(pbU, 0, pbU.Length);
				ms.WriteByte((byte)iLayer);
				if(pbExtra != null) ms.Write(pbExtra, 0, pbExtra.Length);

				using(HMACSHA512 h = new HMACSHA512(pbKey))
				{
					byte[] pbMac = h.ComputeHash(ms.ToArray());
					byte[] pbOut = new byte[cb];
					Array.Copy(pbMac, pbOut, cb);
					MemUtil.ZeroByteArray(pbMac);
					return pbOut;
				}
			}
		}

		private IKeystream CreateKeystream(int iLayer, byte[] pbKey, byte[] pbIV)
		{
			CascadeLayer c = m_layers[iLayer];
			byte[] pbLayerKey = Derive(pbKey, "KPC/cascade/v1/key", iLayer, null, 32);
			try
			{
				if(c == CascadeLayer.ChaCha20)
				{
					byte[] pbNonce = Derive(pbKey, "KPC/cascade/v1/nonce", iLayer, pbIV, 12);
					ChaCha7539Engine e = new ChaCha7539Engine();
					e.Init(true, new ParametersWithIV(new KeyParameter(pbLayerKey), pbNonce));
					return new StreamCipherKeystream(e);
				}

				IBlockCipher bc;
				switch(c)
				{
					case CascadeLayer.Aes: bc = new AesEngine(); break;
					case CascadeLayer.Serpent: bc = new SerpentEngine(); break;
					case CascadeLayer.Twofish: bc = new TwofishEngine(); break;
					case CascadeLayer.Camellia: bc = new CamelliaEngine(); break;
					default: throw new InvalidOperationException();
				}
				bc.Init(true, new KeyParameter(pbLayerKey));
				byte[] pbCtr = Derive(pbKey, "KPC/cascade/v1/nonce", iLayer, pbIV, 16);
				return new CtrKeystream(bc, pbCtr);
			}
			finally { MemUtil.ZeroByteArray(pbLayerKey); }
		}
	}

	internal interface IKeystream
	{
		void Xor(byte[] pb, int iOffset, int cb);
		void Clear();
	}

	internal sealed class StreamCipherKeystream : IKeystream
	{
		private IStreamCipher m_c;
		public StreamCipherKeystream(IStreamCipher c) { m_c = c; }

		public void Xor(byte[] pb, int iOffset, int cb)
		{
			m_c.ProcessBytes(pb, iOffset, cb, pb, iOffset);
		}

		public void Clear() { if(m_c != null) { m_c.Reset(); m_c = null; } }
	}

	/// <summary>CTR mode, 128-bit big-endian counter.</summary>
	internal sealed class CtrKeystream : IKeystream
	{
		private IBlockCipher m_bc;
		private readonly byte[] m_pbCtr;
		private readonly byte[] m_pbBlock = new byte[16];
		private int m_iPos = 16;

		public CtrKeystream(IBlockCipher bc, byte[] pbCtr)
		{
			if(bc.GetBlockSize() != 16) throw new ArgumentException("bc");
			m_bc = bc;
			m_pbCtr = pbCtr;
		}

		public void Xor(byte[] pb, int iOffset, int cb)
		{
			for(int i = 0; i < cb; ++i)
			{
				if(m_iPos == 16)
				{
					m_bc.ProcessBlock(m_pbCtr, 0, m_pbBlock, 0);
					for(int j = 15; j >= 0; --j) { if(++m_pbCtr[j] != 0) break; }
					m_iPos = 0;
				}
				pb[iOffset + i] ^= m_pbBlock[m_iPos++];
			}
		}

		public void Clear()
		{
			MemUtil.ZeroByteArray(m_pbBlock);
			MemUtil.ZeroByteArray(m_pbCtr);
			m_bc = null;
		}
	}

	/// <summary>XORs a keystream over a read-only or write-only base stream.</summary>
	internal sealed class KeystreamXorStream : Stream
	{
		private Stream m_s;
		private readonly IKeystream m_ks;
		private readonly bool m_bWrite;
		private byte[] m_pbBuf = new byte[0];

		public KeystreamXorStream(Stream s, IKeystream ks, bool bWrite)
		{
			m_s = s; m_ks = ks; m_bWrite = bWrite;
		}

		public override bool CanRead { get { return !m_bWrite; } }
		public override bool CanSeek { get { return false; } }
		public override bool CanWrite { get { return m_bWrite; } }
		public override long Length { get { throw new NotSupportedException(); } }
		public override long Position
		{
			get { throw new NotSupportedException(); }
			set { throw new NotSupportedException(); }
		}

		public override void Flush() { if(m_bWrite && (m_s != null)) m_s.Flush(); }
		public override long Seek(long lOffset, SeekOrigin so) { throw new NotSupportedException(); }
		public override void SetLength(long lValue) { throw new NotSupportedException(); }

		public override int Read(byte[] pb, int iOffset, int cb)
		{
			if(m_bWrite) throw new NotSupportedException();
			int r = m_s.Read(pb, iOffset, cb);
			if(r > 0) m_ks.Xor(pb, iOffset, r);
			return r;
		}

		public override void Write(byte[] pb, int iOffset, int cb)
		{
			if(!m_bWrite) throw new NotSupportedException();
			if(cb <= 0) return;
			if(m_pbBuf.Length < cb) { MemUtil.ZeroByteArray(m_pbBuf); m_pbBuf = new byte[cb]; }
			Array.Copy(pb, iOffset, m_pbBuf, 0, cb);
			m_ks.Xor(m_pbBuf, 0, cb);
			m_s.Write(m_pbBuf, 0, cb);
			Array.Clear(m_pbBuf, 0, cb);
		}

		protected override void Dispose(bool bDisposing)
		{
			if(bDisposing && (m_s != null))
			{
				m_s.Dispose();
				m_s = null;
				m_ks.Clear();
				MemUtil.ZeroByteArray(m_pbBuf);
			}
			base.Dispose(bDisposing);
		}
	}

	public static class CerberusCiphers
	{
		private static bool g_bRegistered = false;

		/// <summary>Presets shown in Database Settings. Any other valid
		/// layout is still accepted when opening a file (decoded from its UUID).</summary>
		public static readonly CascadeLayer[] Recommended = { CascadeLayer.Aes, CascadeLayer.ChaCha20 };
		public static readonly CascadeLayer[] Paranoid = { CascadeLayer.Aes, CascadeLayer.ChaCha20, CascadeLayer.Serpent };

		public static readonly CascadeLayer[][] Presets = new CascadeLayer[][] {
			new[] { CascadeLayer.Serpent },
			new[] { CascadeLayer.Twofish },
			new[] { CascadeLayer.Camellia },
			new[] { CascadeLayer.Aes, CascadeLayer.Serpent },
			new[] { CascadeLayer.Twofish, CascadeLayer.Serpent },
			new[] { CascadeLayer.Serpent, CascadeLayer.Twofish, CascadeLayer.Aes },
			new[] { CascadeLayer.Aes, CascadeLayer.Twofish, CascadeLayer.Serpent },
			new[] { CascadeLayer.ChaCha20, CascadeLayer.Serpent, CascadeLayer.Aes },
			new[] { CascadeLayer.Aes, CascadeLayer.Twofish, CascadeLayer.Serpent, CascadeLayer.Camellia },
			new[] { CascadeLayer.ChaCha20, CascadeLayer.Camellia, CascadeLayer.Twofish, CascadeLayer.Serpent }
		};

		public static void Register()
		{
			if(g_bRegistered) return;
			g_bRegistered = true;

			CipherPool.Resolver = delegate(PwUuid pu) { return CascadeCipherEngine.FromUuid(pu); };
			// Named Cerberus profiles first (same UUID as the bare layout)
			CipherPool.GlobalPool.AddCipher(new CascadeCipherEngine("Cerberus Paranoïa", Paranoid));
			CipherPool.GlobalPool.AddCipher(new CascadeCipherEngine("Cerberus Recommandé", Recommended));
			foreach(CascadeLayer[] v in Presets)
				CipherPool.GlobalPool.AddCipher(new CascadeCipherEngine(v));
		}
	}
}
