using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;

namespace Cerberus.App.Services
{
	/// <summary>Remembered key sources for one database (never any secret).</summary>
	public sealed class KeyAssoc
	{
		public string DatabasePath { get; set; }
		public string KeyFilePath { get; set; }
		public bool CerberusFactors { get; set; }
		public bool WindowsAccount { get; set; }
		public bool UsePassword { get; set; } = true;
		public string FactorFlags { get; set; } // "P", "M", "S" combination
		public int GridSize { get; set; } = 3;
	}

	/// <summary>User preferences, stored in %AppData%\KeePass Cerberus\settings.xml.</summary>
	public sealed class Settings
	{
		public string Theme { get; set; } = "Dark";            // Dark | Light
		public bool PreventScreenCapture { get; set; } = true;
		public int AutoLockMinutes { get; set; } = 5;          // 0 = never
		public bool LockOnSessionLock { get; set; } = true;
		public bool LockOnMinimize { get; set; } = false;
		public int ClipboardClearSeconds { get; set; } = 12;   // 0 = never
		public bool AutoSave { get; set; } = true;
		public bool ShowFolders { get; set; } = false;
		public bool ShowDetailsPane { get; set; } = true;
		public bool RememberKeySources { get; set; } = true;
		/// <summary>Remember factor configuration (grid size, checked factors) as default.</summary>
		public bool RememberFactorConfig { get; set; } = true;
		public string LastFactorFlags { get; set; } = "";
		public int LastGridSize { get; set; } = 3;
		public string LastDatabase { get; set; }
		public List<string> RecentDatabases { get; set; } = new List<string>();
		public List<KeyAssoc> KeySources { get; set; } = new List<KeyAssoc>();
		public double WindowWidth { get; set; } = 1180;
		public double WindowHeight { get; set; } = 740;
		public bool WindowMaximized { get; set; } = false;

		// Generator
		public int GenLength { get; set; } = 20;
		public bool GenUpper { get; set; } = true;
		public bool GenLower { get; set; } = true;
		public bool GenDigits { get; set; } = true;
		public bool GenSymbols { get; set; } = true;
		public bool GenExcludeAmbiguous { get; set; } = true;

		[XmlIgnore] public static Settings Current { get; private set; } = new Settings();

		public static string Folder
		{
			get
			{
				return Path.Combine(Environment.GetFolderPath(
					Environment.SpecialFolder.ApplicationData), "KeePass Cerberus");
			}
		}

		private static string FilePath { get { return Path.Combine(Folder, "settings.xml"); } }

		public static void Load()
		{
			try
			{
				if(File.Exists(FilePath))
				{
					XmlSerializer xs = new XmlSerializer(typeof(Settings));
					using(FileStream fs = File.OpenRead(FilePath))
						Current = (Settings)xs.Deserialize(fs);
				}
			}
			catch(Exception) { Current = new Settings(); }
			if(Current.RecentDatabases == null) Current.RecentDatabases = new List<string>();
			if(Current.KeySources == null) Current.KeySources = new List<KeyAssoc>();
		}

		public static void Save()
		{
			try
			{
				Directory.CreateDirectory(Folder);
				string strTmp = FilePath + ".tmp";
				XmlSerializer xs = new XmlSerializer(typeof(Settings));
				using(FileStream fs = File.Create(strTmp)) xs.Serialize(fs, Current);
				if(File.Exists(FilePath)) File.Replace(strTmp, FilePath, null);
				else File.Move(strTmp, FilePath);
			}
			catch(Exception) { }
		}

		public void AddRecent(string strPath)
		{
			if(string.IsNullOrEmpty(strPath)) return;
			RecentDatabases.RemoveAll(s => string.Equals(s, strPath, StringComparison.OrdinalIgnoreCase));
			RecentDatabases.Insert(0, strPath);
			if(RecentDatabases.Count > 10) RecentDatabases.RemoveRange(10, RecentDatabases.Count - 10);
			LastDatabase = strPath;
		}

		public KeyAssoc GetKeyAssoc(string strDb)
		{
			if(!RememberKeySources || string.IsNullOrEmpty(strDb)) return null;
			return KeySources.Find(a => string.Equals(a.DatabasePath, strDb, StringComparison.OrdinalIgnoreCase));
		}

		public void SetKeyAssoc(KeyAssoc a)
		{
			if(a == null || string.IsNullOrEmpty(a.DatabasePath)) return;
			KeySources.RemoveAll(x => string.Equals(x.DatabasePath, a.DatabasePath, StringComparison.OrdinalIgnoreCase));
			if(RememberKeySources) KeySources.Add(a);
		}
	}
}
