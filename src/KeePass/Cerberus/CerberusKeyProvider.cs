/*
  KeePass Cerberus - stackable extra factors: PIN, pattern, Shamir K-of-N.
  Licensed under the GNU GPL v2 or later (same as KeePass).

  Plugs into KeePass as a standard KeyProvider, so it stacks with the
  native master password and key file / Windows account. The chosen
  factors are combined as
      SHA-256("KPC/factors/v1" || flags || H_pin || H_pattern || H_shamir)
  and KeePass then feeds that into its composite key and KDF (Argon2).
*/

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

using KeePass.App;
using KeePass.UI;

using KeePassLib.Cryptography;
using KeePassLib.Keys;
using KeePassLib.Utility;

namespace KeePass.Cerberus
{
	public sealed class CerberusKeyProvider : KeyProvider
	{
		public const string ProviderName = CerberusFactors.ProviderName;

		public override string Name { get { return ProviderName; } }

		public override byte[] GetKey(KeyProviderQueryContext ctx)
		{
			using(CerberusFactorsForm f = new CerberusFactorsForm(ctx))
			{
				if(UIUtil.ShowDialogNotValue(f, DialogResult.OK)) return null;
				return f.ResultKey;
			}
		}

		// Same derivation as the WPF app (Cerberus.Core)
		internal static byte[] Hash(string strLabel, byte[] pbData)
		{
			return CerberusFactors.Hash(strLabel, pbData);
		}

		internal static byte[] PatternBytes(int n, int[] vSeq)
		{
			return CerberusFactors.PatternBytes(n, vSeq);
		}
	}

	internal sealed class CerberusFactorsForm : Form
	{
		private const string CfgFactors = "Cerberus.Factors";
		private const string CfgGrid = "Cerberus.GridSize";

		private readonly KeyProviderQueryContext m_ctx;
		private readonly bool m_bCreate;

		private CheckBox m_cbPin, m_cbPattern, m_cbShamir;
		private TextBox m_tbPin, m_tbPin2;
		private NumericUpDown m_nudGrid, m_nudK, m_nudN;
		private PatternControl m_pat, m_pat2;
		private Label m_lblShares;
		private TextBox m_tbShareDir;
		private readonly List<ShamirShare> m_lShares = new List<ShamirShare>();

		private byte[] m_pbResult = null;
		public byte[] ResultKey { get { return m_pbResult; } }

		public CerberusFactorsForm(KeyProviderQueryContext ctx)
		{
			m_ctx = ctx;
			m_bCreate = ctx.CreatingNewKey;
			Build();
		}

		private void Build()
		{
			this.Text = "Facteurs Cerberus" + (m_bCreate ? " — création" : string.Empty);
			this.Font = new Font("Segoe UI", 9f);
			this.FormBorderStyle = FormBorderStyle.FixedDialog;
			this.MaximizeBox = false; this.MinimizeBox = false;
			this.StartPosition = FormStartPosition.CenterParent;
			this.ShowInTaskbar = false;
			this.AutoScaleMode = AutoScaleMode.Font;
			this.AutoSize = true;
			this.AutoSizeMode = AutoSizeMode.GrowAndShrink;
			this.Padding = new Padding(12);

			string strCfg = Program.Config.CustomConfig.GetString(CfgFactors, string.Empty) ?? string.Empty;
			int nGrid = (int)Program.Config.CustomConfig.GetLong(CfgGrid, 3);

			FlowLayoutPanel root = new FlowLayoutPanel();
			root.FlowDirection = FlowDirection.TopDown;
			root.AutoSize = true; root.AutoSizeMode = AutoSizeMode.GrowAndShrink;
			root.WrapContents = false;
			this.Controls.Add(root);

			Label lblIntro = new Label();
			lblIntro.AutoSize = true; lblIntro.MaximumSize = new Size(520, 0);
			lblIntro.Text = (m_bCreate ?
				"Cochez les facteurs à ajouter. TOUS seront exigés à chaque ouverture, en plus du mot de passe maître et/ou du fichier clé." :
				"Fournissez exactement les mêmes facteurs que lors de la création de la clé.");
			root.Controls.Add(lblIntro);

			// --- PIN ---
			GroupBox gbPin = NewGroup("PIN");
			m_cbPin = NewCheck("Exiger un code PIN", strCfg.Contains("P"));
			m_tbPin = NewSecret();
			m_tbPin2 = NewSecret();
			FlowLayoutPanel fp = NewRow();
			fp.Controls.Add(m_cbPin); fp.Controls.Add(m_tbPin);
			if(m_bCreate) { fp.Controls.Add(new Label { Text = "Confirmer :", AutoSize = true, Margin = new Padding(6, 6, 0, 0) }); fp.Controls.Add(m_tbPin2); }
			gbPin.Controls.Add(fp);
			root.Controls.Add(gbPin);

			// --- Pattern ---
			GroupBox gbPat = NewGroup("Motif");
			FlowLayoutPanel fpat = NewRow();
			fpat.FlowDirection = FlowDirection.TopDown;
			FlowLayoutPanel fhead = NewRow();
			m_cbPattern = NewCheck("Exiger un motif", strCfg.Contains("M"));
			m_nudGrid = new NumericUpDown { Minimum = 3, Maximum = 8, Value = Math.Max(3, Math.Min(8, nGrid)), Width = 50 };
			Button btnClear = new Button { Text = "Effacer", AutoSize = true };
			fhead.Controls.Add(m_cbPattern);
			fhead.Controls.Add(new Label { Text = "Grille :", AutoSize = true, Margin = new Padding(12, 6, 0, 0) });
			fhead.Controls.Add(m_nudGrid);
			fhead.Controls.Add(btnClear);
			CheckBox cbHide = NewCheck("Masquer le tracé", false);
			cbHide.Margin = new Padding(12, 5, 3, 3);
			fhead.Controls.Add(cbHide);
			fpat.Controls.Add(fhead);

			FlowLayoutPanel fpads = NewRow();
			m_pat = new PatternControl { GridSize = (int)m_nudGrid.Value };
			fpads.Controls.Add(m_pat);
			if(m_bCreate)
			{
				m_pat2 = new PatternControl { GridSize = (int)m_nudGrid.Value };
				fpads.Controls.Add(m_pat2);
			}
			fpat.Controls.Add(fpads);
			if(m_bCreate)
				fpat.Controls.Add(new Label { AutoSize = true, Text = "Dessinez le motif à gauche, puis répétez-le à droite." });
			gbPat.Controls.Add(fpat);
			root.Controls.Add(gbPat);

			m_nudGrid.ValueChanged += delegate
			{
				m_pat.GridSize = (int)m_nudGrid.Value;
				if(m_pat2 != null) m_pat2.GridSize = (int)m_nudGrid.Value;
			};
			btnClear.Click += delegate { m_pat.Clear(); if(m_pat2 != null) m_pat2.Clear(); };
			cbHide.CheckedChanged += delegate
			{
				m_pat.HideTrace = cbHide.Checked;
				if(m_pat2 != null) m_pat2.HideTrace = cbHide.Checked;
			};

			// --- Shamir ---
			GroupBox gbSh = NewGroup("Partage de secret Shamir (K parmi N)");
			FlowLayoutPanel fsh = NewRow();
			m_cbShamir = NewCheck("Exiger des parts Shamir", strCfg.Contains("S"));
			fsh.Controls.Add(m_cbShamir);
			if(m_bCreate)
			{
				m_nudK = new NumericUpDown { Minimum = 2, Maximum = 16, Value = 2, Width = 45 };
				m_nudN = new NumericUpDown { Minimum = 2, Maximum = 16, Value = 3, Width = 45 };
				m_tbShareDir = new TextBox { Width = 170, ReadOnly = true };
				Button btnDir = new Button { Text = "Dossier…", AutoSize = true };
				btnDir.Click += delegate
				{
					using(FolderBrowserDialog fbd = new FolderBrowserDialog())
					{
						fbd.Description = "Dossier où écrire les parts (à déplacer ensuite vers des supports SÉPARÉS)";
						if(fbd.ShowDialog(this) == DialogResult.OK) m_tbShareDir.Text = fbd.SelectedPath;
					}
				};
				fsh.Controls.Add(new Label { Text = "K =", AutoSize = true, Margin = new Padding(8, 6, 0, 0) });
				fsh.Controls.Add(m_nudK);
				fsh.Controls.Add(new Label { Text = "N =", AutoSize = true, Margin = new Padding(4, 6, 0, 0) });
				fsh.Controls.Add(m_nudN);
				fsh.Controls.Add(m_tbShareDir);
				fsh.Controls.Add(btnDir);
			}
			else
			{
				Button btnLoad = new Button { Text = "Charger les parts…", AutoSize = true };
				m_lblShares = new Label { AutoSize = true, Text = "Aucune part chargée.", Margin = new Padding(6, 6, 0, 0) };
				btnLoad.Click += this.OnLoadShares;
				fsh.Controls.Add(btnLoad);
				fsh.Controls.Add(m_lblShares);
			}
			gbSh.Controls.Add(fsh);
			root.Controls.Add(gbSh);

			// --- Buttons ---
			FlowLayoutPanel fb = NewRow();
			fb.Anchor = AnchorStyles.Right;
			Button btnOK = new Button { Text = "OK", DialogResult = DialogResult.None, Width = 90 };
			Button btnCancel = new Button { Text = "Annuler", DialogResult = DialogResult.Cancel, Width = 90 };
			btnOK.Click += this.OnOK;
			fb.Controls.Add(btnOK); fb.Controls.Add(btnCancel);
			CheckBox cbCap = CerberusCapture.CreateCheckBox();
			cbCap.Margin = new Padding(24, 6, 0, 0);
			fb.Controls.Add(cbCap);
			root.Controls.Add(fb);
			this.AcceptButton = btnOK;
			this.CancelButton = btnCancel;
		}

		protected override void OnHandleCreated(EventArgs e)
		{
			base.OnHandleCreated(e);
			// Hidden from screenshots / recording / screen sharing when the
			// protection is enabled (Affichage menu or Options).
			GlobalWindowManager.SetCaptureProtection(this.Handle,
				Program.Config.Security.PreventScreenCapture);
		}

		private static GroupBox NewGroup(string strText)
		{
			return new GroupBox { Text = strText, AutoSize = true,
				AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(520, 0),
				Padding = new Padding(8) };
		}

		private static FlowLayoutPanel NewRow()
		{
			return new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
				WrapContents = false, Dock = DockStyle.Top };
		}

		private static CheckBox NewCheck(string strText, bool bChecked)
		{
			return new CheckBox { Text = strText, AutoSize = true, Checked = bChecked,
				Margin = new Padding(3, 5, 3, 3) };
		}

		private static TextBox NewSecret()
		{
			return new TextBox { UseSystemPasswordChar = true, Width = 110, MaxLength = 32 };
		}

		private void OnLoadShares(object sender, EventArgs e)
		{
			using(OpenFileDialog ofd = new OpenFileDialog())
			{
				ofd.Title = "Sélectionner au moins K parts Shamir";
				ofd.Filter = "Parts Cerberus (*.kpcshare)|*.kpcshare|Tous les fichiers (*.*)|*.*";
				ofd.Multiselect = true;
				if(ofd.ShowDialog(this) != DialogResult.OK) return;

				m_lShares.Clear();
				try
				{
					foreach(string strFile in ofd.FileNames)
						m_lShares.Add(ShamirShare.Parse(File.ReadAllText(strFile, Encoding.UTF8)));
					int k = ((m_lShares.Count > 0) ? m_lShares[0].K : 0);
					m_lblShares.Text = m_lShares.Count.ToString() + " part(s) chargée(s), " +
						k.ToString() + " requise(s).";
				}
				catch(Exception ex)
				{
					m_lShares.Clear();
					m_lblShares.Text = "Aucune part chargée.";
					MessageService.ShowWarning(ex.Message);
				}
			}
		}

		private void OnOK(object sender, EventArgs e)
		{
			if(!m_cbPin.Checked && !m_cbPattern.Checked && !m_cbShamir.Checked)
			{
				MessageService.ShowWarning("Cochez au moins un facteur.");
				return;
			}

			List<byte[]> lParts = new List<byte[]>();
			byte bFlags = 0;
			try
			{
				if(m_cbPin.Checked)
				{
					string strPin = m_tbPin.Text;
					if(strPin.Length == 0)
					{ MessageService.ShowWarning("Saisissez le PIN (ou décochez ce facteur)."); return; }
					if(m_bCreate && (strPin != m_tbPin2.Text))
					{ MessageService.ShowWarning("Les deux PIN ne correspondent pas."); return; }
					byte[] pb = StrUtil.Utf8.GetBytes(strPin);
					lParts.Add(CerberusKeyProvider.Hash("KPC/pin/v1", pb));
					MemUtil.ZeroByteArray(pb);
					bFlags |= 1;
				}

				if(m_cbPattern.Checked)
				{
					int[] v = m_pat.Sequence;
					if(v.Length == 0)
					{ MessageService.ShowWarning("Dessinez le motif (ou décochez ce facteur)."); return; }
					if(m_bCreate && !MemUtil.ArraysEqual(
						CerberusKeyProvider.PatternBytes(m_pat.GridSize, v),
						CerberusKeyProvider.PatternBytes(m_pat2.GridSize, m_pat2.Sequence)))
					{ MessageService.ShowWarning("Les deux motifs ne correspondent pas."); return; }
					byte[] pb = CerberusKeyProvider.PatternBytes(m_pat.GridSize, v);
					lParts.Add(CerberusKeyProvider.Hash("KPC/pattern/v1", pb));
					MemUtil.ZeroByteArray(pb);
					bFlags |= 2;
				}

				if(m_cbShamir.Checked)
				{
					byte[] pbSecret = (m_bCreate ? CreateShares() : CombineShares());
					if(pbSecret == null) return;
					lParts.Add(CerberusKeyProvider.Hash("KPC/shamir/v1", pbSecret));
					MemUtil.ZeroByteArray(pbSecret);
					bFlags |= 4;
				}

				using(MemoryStream ms = new MemoryStream())
				{
					byte[] pbLabel = Encoding.ASCII.GetBytes("KPC/factors/v1");
					ms.Write(pbLabel, 0, pbLabel.Length);
					ms.WriteByte(bFlags);
					foreach(byte[] pb in lParts) ms.Write(pb, 0, pb.Length);
					byte[] pbAll = ms.ToArray();
					m_pbResult = CerberusKeyProvider.Hash("KPC/key/v1", pbAll);
					MemUtil.ZeroByteArray(pbAll);
				}
			}
			finally
			{
				foreach(byte[] pb in lParts) MemUtil.ZeroByteArray(pb);
			}

			string strCfg = (m_cbPin.Checked ? "P" : "") + (m_cbPattern.Checked ? "M" : "") +
				(m_cbShamir.Checked ? "S" : "");
			Program.Config.CustomConfig.SetString(CfgFactors, strCfg);
			Program.Config.CustomConfig.SetLong(CfgGrid, (long)m_nudGrid.Value);

			if(m_bCreate && (m_cbPin.Checked || m_cbPattern.Checked))
				MessageService.ShowInfo("Le PIN et le motif sont des protections simples : " +
					"ils complètent le mot de passe maître sans le remplacer." +
					MessageService.NewParagraph + "Aucune longueur minimale n'est imposée ; " +
					"plus ils sont longs, plus ils résistent.");

			this.DialogResult = DialogResult.OK;
			Close();
		}

		private byte[] CombineShares()
		{
			if(m_lShares.Count == 0)
			{ MessageService.ShowWarning("Chargez les fichiers de parts Shamir."); return null; }
			try { return Shamir.Combine(m_lShares); }
			catch(Exception ex) { MessageService.ShowWarning(ex.Message); return null; }
		}

		private byte[] CreateShares()
		{
			int k = (int)m_nudK.Value, n = (int)m_nudN.Value;
			if(n < k) { MessageService.ShowWarning("N doit être supérieur ou égal à K."); return null; }
			string strDir = m_tbShareDir.Text;
			if(string.IsNullOrEmpty(strDir) || !Directory.Exists(strDir))
			{ MessageService.ShowWarning("Choisissez le dossier où écrire les parts."); return null; }

			string strBase = UrlUtil.StripExtension(UrlUtil.GetFileName(m_ctx.DatabasePath));
			if(string.IsNullOrEmpty(strBase)) strBase = "base";

			byte[] pbSecret = CryptoRandom.Instance.GetRandomBytes(32);
			ShamirShare[] v = Shamir.Split(pbSecret, k, n);
			List<string> lFiles = new List<string>();
			try
			{
				for(int i = 0; i < n; ++i)
				{
					string strFile = Path.Combine(strDir, strBase + ".part" + (i + 1).ToString() +
						"of" + n.ToString() + ".kpcshare");
					if(File.Exists(strFile))
						throw new IOException("Le fichier existe déjà : " + strFile);
					File.WriteAllText(strFile, v[i].Serialize(), new UTF8Encoding(false));
					lFiles.Add(strFile);
				}
			}
			catch(Exception ex)
			{
				foreach(string f in lFiles) { try { File.Delete(f); } catch(Exception) { } }
				MemUtil.ZeroByteArray(pbSecret);
				MessageService.ShowWarning(ex.Message);
				return null;
			}

			MessageService.ShowInfo(n.ToString() + " parts écrites dans :" + MessageService.NewLine + strDir +
				MessageService.NewParagraph + k.ToString() + " d'entre elles seront exigées à chaque ouverture." +
				MessageService.NewParagraph + "Déplacez-les vers des supports SÉPARÉS (clés USB, coffre…). " +
				"Moins de " + k.ToString() + " parts = base irrécupérable.");
			return pbSecret;
		}
	}
}
