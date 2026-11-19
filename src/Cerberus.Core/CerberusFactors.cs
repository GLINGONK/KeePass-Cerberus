/*
  KeePass Cerberus - key derivation of the extra factors (PIN, pattern,
  Shamir). UI-independent: used by the WPF app and the legacy UI so both
  produce exactly the same key.
  Licensed under the GNU GPL v2 or later (same as KeePass).

      H(label, data)   = SHA-256(ASCII(label) || data)
      part_pin         = H("KPC/pin/v1",     UTF-8(pin))
      part_pattern     = H("KPC/pattern/v1", gridSize || indices...)
      part_shamir      = H("KPC/shamir/v1",  reconstructed secret)
      key              = H("KPC/key/v1", "KPC/factors/v1" || flags || parts in that order)
      flags            = 1 (PIN) | 2 (pattern) | 4 (Shamir)

  The resulting 32 bytes are added to the KeePass composite key as a
  KcpCustomKey (hashed again by KeePass), then go through the KDF.
*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

using KeePassLib.Keys;
using KeePassLib.Utility;

namespace KeePass.Cerberus
{
	public static class CerberusFactors
	{
		/// <summary>Name of the KcpCustomKey / key provider (must never change).</summary>
		public const string ProviderName = "Cerberus (PIN / Motif / Shamir)";

		public static byte[] Hash(string strLabel, byte[] pbData)
		{
			byte[] pbLabel = Encoding.ASCII.GetBytes(strLabel);
			byte[] pb = new byte[pbLabel.Length + pbData.Length];
			Array.Copy(pbLabel, pb, pbLabel.Length);
			Array.Copy(pbData, 0, pb, pbLabel.Length, pbData.Length);
			try { using(SHA256Managed h = new SHA256Managed()) return h.ComputeHash(pb); }
			finally { MemUtil.ZeroByteArray(pb); }
		}

		public static byte[] PatternBytes(int nGrid, int[] vSeq)
		{
			byte[] pb = new byte[vSeq.Length + 1];
			pb[0] = (byte)nGrid;
			for(int i = 0; i < vSeq.Length; ++i) pb[i + 1] = (byte)vSeq[i];
			return pb;
		}

		/// <summary>
		/// Combine the chosen factors. Pass null for unused ones. Returns null
		/// if no factor is given.
		/// </summary>
		public static byte[] Derive(byte[] pbPinUtf8, int nGrid, int[] vPattern, byte[] pbShamirSecret)
		{
			List<byte[]> lParts = new List<byte[]>();
			byte bFlags = 0;
			try
			{
				if(pbPinUtf8 != null)
				{
					lParts.Add(Hash("KPC/pin/v1", pbPinUtf8));
					bFlags |= 1;
				}
				if(vPattern != null)
				{
					byte[] pb = PatternBytes(nGrid, vPattern);
					lParts.Add(Hash("KPC/pattern/v1", pb));
					MemUtil.ZeroByteArray(pb);
					bFlags |= 2;
				}
				if(pbShamirSecret != null)
				{
					lParts.Add(Hash("KPC/shamir/v1", pbShamirSecret));
					bFlags |= 4;
				}
				if(bFlags == 0) return null;

				using(MemoryStream ms = new MemoryStream())
				{
					byte[] pbLabel = Encoding.ASCII.GetBytes("KPC/factors/v1");
					ms.Write(pbLabel, 0, pbLabel.Length);
					ms.WriteByte(bFlags);
					foreach(byte[] pb in lParts) ms.Write(pb, 0, pb.Length);
					byte[] pbAll = ms.ToArray();
					try { return Hash("KPC/key/v1", pbAll); }
					finally { MemUtil.ZeroByteArray(pbAll); }
				}
			}
			finally { foreach(byte[] pb in lParts) MemUtil.ZeroByteArray(pb); }
		}

		/// <summary>KeePass user key for the given derived factor key.</summary>
		public static KcpCustomKey ToUserKey(byte[] pbDerived)
		{
			return new KcpCustomKey(ProviderName, pbDerived, true);
		}
	}
}
