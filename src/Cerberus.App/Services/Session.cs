using System;
using System.Collections.Generic;
using System.Linq;

using KeePass.Cerberus;

using KeePassLib;
using KeePassLib.Cryptography.Cipher;
using KeePassLib.Cryptography.KeyDerivation;
using KeePassLib.Interfaces;
using KeePassLib.Keys;
using KeePassLib.Serialization;

namespace Cerberus.App.Services
{
	/// <summary>The open database (one at a time).</summary>
	internal static class Session
	{
		public static PwDatabase Db { get; private set; }
		public static string Path { get; private set; }
		public static bool IsOpen { get { return Db != null && Db.IsOpen; } }

		public static event Action Changed;

		public static void Init()
		{
			CerberusCiphers.Register();
		}

		public static void RaiseChanged() { Changed?.Invoke(); }

		public static void SetPath(string strPath) { Path = strPath; }

		/// <summary>Open (blocking: call from a worker thread).</summary>
		public static PwDatabase OpenFile(string strPath, CompositeKey ck)
		{
			PwDatabase db = new PwDatabase();
			db.Open(IOConnectionInfo.FromPath(strPath), ck, new NullStatusLogger());
			return db;
		}

		public static void Attach(PwDatabase db, string strPath)
		{
			Close();
			Db = db;
			Path = strPath;
			Settings.Current.AddRecent(strPath);
			Settings.Save();
			RaiseChanged();
		}

		public static void Close()
		{
			if(Db != null)
			{
				try { Db.Close(); } catch(Exception) { }
			}
			Db = null;
			Path = null;
			RaiseChanged();
		}

		/// <summary>Save (blocking).</summary>
		public static void Save()
		{
			if(!IsOpen) return;
			Db.Save(new NullStatusLogger());
		}

		public static void MarkModified(bool bAutoSave, Action<Exception> onError)
		{
			if(!IsOpen) return;
			Db.Modified = true;
			if(bAutoSave)
			{
				try { Save(); }
				catch(Exception ex) { onError?.Invoke(ex); }
			}
			RaiseChanged();
		}

		public static PwGroup RecycleBin
		{
			get
			{
				if(!IsOpen || !Db.RecycleBinEnabled) return null;
				return Db.RootGroup.FindGroup(Db.RecycleBinUuid, true);
			}
		}

		public static IEnumerable<PwEntry> AllEntries(bool bIncludeBin)
		{
			if(!IsOpen) yield break;
			PwGroup pgBin = RecycleBin;
			foreach(PwEntry pe in Db.RootGroup.GetEntries(true))
			{
				if(!bIncludeBin && pgBin != null && pe.IsContainedIn(pgBin)) continue;
				yield return pe;
			}
		}

		/// <summary>Recycle bin semantics of KeePass.</summary>
		public static void DeleteEntry(PwEntry pe)
		{
			PwGroup pgParent = pe.ParentGroup;
			if(pgParent == null) return;
			PwGroup pgBin = RecycleBin;
			bool bPermanent = !Db.RecycleBinEnabled || (pgBin != null && pe.IsContainedIn(pgBin));
			if(bPermanent)
			{
				pgParent.Entries.Remove(pe);
				Db.DeletedObjects.Add(new PwDeletedObject(pe.Uuid, DateTime.UtcNow));
				return;
			}
			if(pgBin == null)
			{
				pgBin = new PwGroup(true, true, L.T("Corbeille"), PwIcon.TrashBin);
				pgBin.EnableAutoType = false;
				pgBin.EnableSearching = false;
				pgBin.IsExpanded = false;
				Db.RootGroup.AddGroup(pgBin, true);
				Db.RecycleBinUuid = pgBin.Uuid;
				Db.RecycleBinChanged = DateTime.UtcNow;
			}
			pgParent.Entries.Remove(pe);
			pgBin.AddEntry(pe, true, true);
			pe.Touch(false);
		}

		// ---- Security profiles (same values as the original Cerberus) ----

		public sealed class Profile
		{
			public string Name;
			public string Description;
			public CascadeLayer[] Layers;   // null = AES-256 only (KeePass compatible)
			public ulong MemoryMiB;
			public ulong Iterations;
			public uint Parallelism;
			public override string ToString() { return Name; }
		}

		public static readonly Profile[] Profiles = new Profile[] {
			new Profile { Name = L.T("Compatible KeePass"), Description = L.T("AES-256 · Argon2id 64 Mio · s'ouvre aussi dans KeePass d'origine"),
				Layers = null, MemoryMiB = 64, Iterations = 2, Parallelism = 2 },
			new Profile { Name = L.T("Interactif"), Description = L.T("AES-256 → ChaCha20 · Argon2id 256 Mio · ~0,5 s"),
				Layers = CerberusCiphers.Recommended, MemoryMiB = 256, Iterations = 3, Parallelism = 4 },
			new Profile { Name = L.T("Renforcé (recommandé)"), Description = L.T("AES-256 → ChaCha20 · Argon2id 1 Gio · ~2 s"),
				Layers = CerberusCiphers.Recommended, MemoryMiB = 1024, Iterations = 4, Parallelism = 4 },
			new Profile { Name = L.T("Paranoïa"), Description = L.T("AES-256 → ChaCha20 → Serpent-256 · Argon2id 4 Gio · ~11 s, 6 Go de RAM libre"),
				Layers = CerberusCiphers.Paranoid, MemoryMiB = 4096, Iterations = 8, Parallelism = 8 }
		};

		public static void ApplyProfile(PwDatabase db, Profile p)
		{
			db.DataCipherUuid = (p.Layers == null) ? StandardAesEngine.AesUuid
				: new CascadeCipherEngine(p.Layers).CipherUuid;
			db.KdfParameters = MakeArgon2(p.MemoryMiB, p.Iterations, p.Parallelism);
		}

		public static KdfParameters MakeArgon2(ulong uMemMiB, ulong uIt, uint uPar)
		{
			KdfParameters kp = new Argon2Kdf(Argon2Type.ID).GetDefaultParameters();
			kp.SetUInt64(Argon2Kdf.ParamMemory, uMemMiB * 1024UL * 1024UL);
			kp.SetUInt64(Argon2Kdf.ParamIterations, uIt);
			kp.SetUInt32(Argon2Kdf.ParamParallelism, uPar);
			return kp;
		}

		public static string CipherName(PwUuid pu)
		{
			ICipherEngine c = CipherPool.GlobalPool.GetCipher(pu);
			return (c != null) ? c.DisplayName : L.T("Inconnu");
		}

		public static string DescribeKdf(KdfParameters p)
		{
			KdfEngine k = KdfPool.Get(p.KdfUuid);
			if(k == null) return L.T("Inconnue");
			if(k is Argon2Kdf)
			{
				ulong m = p.GetUInt64(Argon2Kdf.ParamMemory, 0) / (1024UL * 1024UL);
				return k.Name + " · " + (m >= 1024 ? (m / 1024).ToString() + L.T(" Gio") : m.ToString() + L.T(" Mio")) +
					" · " + p.GetUInt64(Argon2Kdf.ParamIterations, 0) + L.T(" passes · ") +
					p.GetUInt32(Argon2Kdf.ParamParallelism, 0) + " threads";
			}
			if(k is AesKdf) return "AES-KDF · " + p.GetUInt64(AesKdf.ParamRounds, 0).ToString("N0") + L.T(" tours");
			return k.Name;
		}
	}
}
