/*
  KeePass Cerberus - Shamir K-of-N secret sharing over GF(2^8).
  Licensed under the GNU GPL v2 or later (same as KeePass).

  Share file format (UTF-8 text):
      KPC-SHARE-1
      k=<threshold>
      x=<share index 1..255>
      id=<hex, 8 bytes = SHA-256(secret)[0..8], identifies the share set>
      y=<hex share bytes>
*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

using KeePassLib.Cryptography;
using KeePassLib.Utility;

namespace KeePass.Cerberus
{
	public sealed class ShamirShare
	{
		public int K;
		public byte X;
		public byte[] SetId;
		public byte[] Y;

		private const string Header = "KPC-SHARE-1";

		public string Serialize()
		{
			StringBuilder sb = new StringBuilder();
			sb.Append(Header).Append('\n');
			sb.Append("k=").Append(K).Append('\n');
			sb.Append("x=").Append(X).Append('\n');
			sb.Append("id=").Append(MemUtil.ByteArrayToHexString(SetId)).Append('\n');
			sb.Append("y=").Append(MemUtil.ByteArrayToHexString(Y)).Append('\n');
			return sb.ToString();
		}

		public static ShamirShare Parse(string strText)
		{
			if(strText == null) throw new ArgumentNullException("strText");
			string[] v = strText.Replace("\r", string.Empty).Split('\n');
			if((v.Length < 5) || (v[0].Trim() != Header))
				throw new FormatException("Fichier de part Shamir invalide.");

			ShamirShare s = new ShamirShare();
			foreach(string strLine in v)
			{
				int i = strLine.IndexOf('=');
				if(i <= 0) continue;
				string k = strLine.Substring(0, i).Trim(), val = strLine.Substring(i + 1).Trim();
				if(k == "k") s.K = int.Parse(val);
				else if(k == "x") s.X = byte.Parse(val);
				else if(k == "id") s.SetId = MemUtil.HexStringToByteArray(val);
				else if(k == "y") s.Y = MemUtil.HexStringToByteArray(val);
			}
			if((s.K < 2) || (s.X == 0) || (s.SetId == null) || (s.SetId.Length != 8) ||
				(s.Y == null) || (s.Y.Length == 0))
				throw new FormatException("Fichier de part Shamir incomplet.");
			return s;
		}
	}

	public static class Shamir
	{
		private static readonly byte[] g_Exp = new byte[512];
		private static readonly byte[] g_Log = new byte[256];

		static Shamir()
		{
			// Generator 3 over the AES polynomial x^8+x^4+x^3+x+1.
			int x = 1;
			for(int i = 0; i < 255; ++i)
			{
				g_Exp[i] = (byte)x;
				g_Log[x] = (byte)i;
				int x2 = x << 1;
				if((x2 & 0x100) != 0) x2 ^= 0x11B;
				x ^= x2; // x * 3
			}
			for(int i = 255; i < 512; ++i) g_Exp[i] = g_Exp[i - 255];
		}

		private static byte Mul(byte a, byte b)
		{
			if((a == 0) || (b == 0)) return 0;
			return g_Exp[g_Log[a] + g_Log[b]];
		}

		private static byte Div(byte a, byte b)
		{
			if(b == 0) throw new DivideByZeroException();
			if(a == 0) return 0;
			return g_Exp[g_Log[a] + 255 - g_Log[b]];
		}

		public static byte[] SetIdOf(byte[] pbSecret)
		{
			using(SHA256Managed h = new SHA256Managed())
			{
				byte[] pbHash = h.ComputeHash(pbSecret);
				byte[] pbId = new byte[8];
				Array.Copy(pbHash, pbId, 8);
				return pbId;
			}
		}

		public static ShamirShare[] Split(byte[] pbSecret, int k, int n)
		{
			if((pbSecret == null) || (pbSecret.Length == 0)) throw new ArgumentException("pbSecret");
			if((k < 2) || (n < k) || (n > 255)) throw new ArgumentOutOfRangeException("k/n");

			byte[] pbId = SetIdOf(pbSecret);
			ShamirShare[] v = new ShamirShare[n];
			for(int i = 0; i < n; ++i)
				v[i] = new ShamirShare { K = k, X = (byte)(i + 1), SetId = pbId,
					Y = new byte[pbSecret.Length] };

			byte[] pbCoef = new byte[k];
			for(int b = 0; b < pbSecret.Length; ++b)
			{
				pbCoef[0] = pbSecret[b];
				byte[] pbRnd = CryptoRandom.Instance.GetRandomBytes((uint)(k - 1));
				Array.Copy(pbRnd, 0, pbCoef, 1, k - 1);
				MemUtil.ZeroByteArray(pbRnd);

				for(int i = 0; i < n; ++i)
				{
					// Horner evaluation at x = i + 1
					byte x = (byte)(i + 1), y = 0;
					for(int c = k - 1; c >= 0; --c) y = (byte)(Mul(y, x) ^ pbCoef[c]);
					v[i].Y[b] = y;
				}
			}
			MemUtil.ZeroByteArray(pbCoef);
			return v;
		}

		public static byte[] Combine(IList<ShamirShare> lShares)
		{
			if((lShares == null) || (lShares.Count == 0))
				throw new ArgumentException("Aucune part Shamir.");

			ShamirShare s0 = lShares[0];
			int k = s0.K, cb = s0.Y.Length;
			List<ShamirShare> l = new List<ShamirShare>();
			foreach(ShamirShare s in lShares)
			{
				if((s.K != k) || (s.Y.Length != cb) || !MemUtil.ArraysEqual(s.SetId, s0.SetId))
					throw new InvalidDataException("Les parts n'appartiennent pas au même jeu.");
				bool bDup = false;
				foreach(ShamirShare t in l) { if(t.X == s.X) bDup = true; }
				if(!bDup) l.Add(s);
			}
			if(l.Count < k)
				throw new InvalidDataException("Parts insuffisantes : " + l.Count.ToString() +
					" fournie(s), " + k.ToString() + " requise(s).");
			if(l.Count > k) l.RemoveRange(k, l.Count - k);

			byte[] pbSecret = new byte[cb];
			for(int i = 0; i < k; ++i)
			{
				// Lagrange basis at x = 0: prod_j x_j / (x_j - x_i); minus == xor in GF(2^8)
				byte li = 1;
				for(int j = 0; j < k; ++j)
				{
					if(j == i) continue;
					li = Mul(li, Div(l[j].X, (byte)(l[j].X ^ l[i].X)));
				}
				for(int b = 0; b < cb; ++b) pbSecret[b] ^= Mul(li, l[i].Y[b]);
			}

			if(!MemUtil.ArraysEqual(SetIdOf(pbSecret), s0.SetId))
			{
				MemUtil.ZeroByteArray(pbSecret);
				throw new InvalidDataException("Reconstruction Shamir incohérente (part corrompue ?).");
			}
			return pbSecret;
		}
	}
}
