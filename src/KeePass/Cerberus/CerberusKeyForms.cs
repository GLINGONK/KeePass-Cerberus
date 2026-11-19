/*
  KeePass Cerberus - redesigned master key dialogs (open / create).
  Licensed under the GNU GPL v2 or later (same as KeePass).

  Replaces KeyPromptForm / KeyCreationForm. Unlike the original dialogs,
  a key file AND the Cerberus factors can be combined. User keys are
  added in the same order KeePass always used (password, key file or
  provider, Windows account), so existing databases open unchanged.
*/

using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

using KeePass.App;
using KeePass.App.Configuration;
using KeePass.Forms;
using KeePass.Resources;
using KeePass.UI;
using KeePass.Util;

using KeePassLib.Cryptography;
using KeePassLib.Native;
using KeePassLib.Keys;
using KeePassLib.Serialization;
using KeePassLib.Utility;

namespace KeePass.Cerberus
{
	/// <summary>Shared look: header, sections, accent button.</summary>
	internal abstract class CerberusDialog : Form
	{
		protected readonly TableLayoutPanel m_body;
		protected readonly FlowLayoutPanel m_buttons;
		private readonly Label m_lblTitle, m_lblSub;

		protected CerberusDialog(string strTitle, string strSubtitle)
		{
			this.Text = PwDefsShort + " — " + strTitle;
			this.Font = CerberusTheme.UiFont(9.75f, FontStyle.Regular);
			this.FormBorderStyle = FormBorderStyle.FixedDialog;
			this.MaximizeBox = false; this.MinimizeBox = false;
			this.StartPosition = FormStartPosition.CenterScreen;
			this.ShowInTaskbar = true;
			this.AutoScaleMode = AutoScaleMode.Dpi;
			this.AutoSize = true;
			this.AutoSizeMode = AutoSizeMode.GrowAndShrink;
			this.Padding = new Padding(0);
			try { this.Icon = AppIcons.Default; } catch(Exception) { }

			TableLayoutPanel root = new TableLayoutPanel();
			root.AutoSize = true; root.AutoSizeMode = AutoSizeMode.GrowAndShrink;
			root.ColumnCount = 1; root.Dock = DockStyle.Fill;
			root.Padding = new Padding(0);
			this.Controls.Add(root);

			// Header
			Panel pnlHead = new Panel();
			pnlHead.Height = 78; pnlHead.Width = 520;
			pnlHead.Margin = new Padding(0);
			pnlHead.BackColor = CerberusTheme.Panel;
			pnlHead.Paint += delegate(object s, PaintEventArgs e)
			{
				using(SolidBrush br = new SolidBrush(CerberusTheme.Accent))
					e.Graphics.FillRectangle(br, 0, pnlHead.Height - 3, pnlHead.Width, 3);
				using(Font f = new Font("Segoe MDL2 Assets", 22f))
					TextRenderer.DrawText(e.Graphics, "", f, new Rectangle(18, 0, 44, pnlHead.Height - 3),
						CerberusTheme.Accent, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
			};
			m_lblTitle = new Label { Text = strTitle, AutoSize = true, Location = new Point(72, 14),
				Font = CerberusTheme.UiFont(15f, FontStyle.Regular), ForeColor = CerberusTheme.Text, BackColor = Color.Transparent };
			m_lblSub = new Label { Text = strSubtitle, AutoSize = false, Location = new Point(74, 46),
				Size = new Size(430, 20), AutoEllipsis = true,
				Font = CerberusTheme.UiFont(9f, FontStyle.Regular), ForeColor = CerberusTheme.Muted, BackColor = Color.Transparent };
			pnlHead.Controls.Add(m_lblTitle);
			pnlHead.Controls.Add(m_lblSub);
			root.Controls.Add(pnlHead);

			m_body = new TableLayoutPanel();
			m_body.AutoSize = true; m_body.AutoSizeMode = AutoSizeMode.GrowAndShrink;
			m_body.ColumnCount = 1;
			m_body.Padding = new Padding(22, 14, 22, 6);
			m_body.Margin = new Padding(0);
			root.Controls.Add(m_body);

			m_buttons = new FlowLayoutPanel();
			m_buttons.FlowDirection = FlowDirection.RightToLeft;
			m_buttons.AutoSize = true; m_buttons.AutoSizeMode = AutoSizeMode.GrowAndShrink;
			m_buttons.Dock = DockStyle.Fill;
			m_buttons.Padding = new Padding(16, 6, 16, 16);
			m_buttons.Margin = new Padding(0);
			root.Controls.Add(m_buttons);
		}

		private static string PwDefsShort { get { return KeePassLib.PwDefs.ShortProductName; } }

		protected override void OnLoad(EventArgs e)
		{
			base.OnLoad(e);
			CheckBox cbCap = CerberusCapture.CreateCheckBox();
			cbCap.Margin = new Padding(0, 8, 40, 0);
			m_buttons.Controls.Add(cbCap);
			CerberusCapture.Changed += delegate { if(!IsDisposed && IsHandleCreated) GlobalWindowManager.SetCaptureProtection(Handle, CerberusCapture.Enabled); };
			CerberusTheme.Apply(this);
			GlobalWindowManager.SetCaptureProtection(this.Handle,
				Program.Config.Security.PreventScreenCapture);
			StyleButtons();
		}

		protected abstract void StyleButtons();

		protected const int ContentWidth = 476;

		protected Label AddHeading(string strText)
		{
			Label l = new Label { Text = strText.ToUpperInvariant(), AutoSize = true,
				Font = CerberusTheme.UiFont(8f, FontStyle.Bold), ForeColor = CerberusTheme.Muted,
				Margin = new Padding(0, 12, 0, 4) };
			m_body.Controls.Add(l);
			return l;
		}

		protected Label AddNote(string strText)
		{
			Label l = new Label { Text = strText, AutoSize = true, MaximumSize = new Size(ContentWidth, 0),
				Font = CerberusTheme.UiFont(8.5f, FontStyle.Regular), ForeColor = CerberusTheme.Muted,
				Margin = new Padding(22, 0, 0, 4) };
			m_body.Controls.Add(l);
			return l;
		}

		/// <summary>Password box with an eye toggle.</summary>
		protected SecureTextBoxEx AddPasswordRow(out CheckBox cbEye)
		{
			TableLayoutPanel t = new TableLayoutPanel { ColumnCount = 2, AutoSize = true,
				Margin = new Padding(0, 0, 0, 4), Width = ContentWidth };
			t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ContentWidth - 44));
			t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 44));

			SecureTextBoxEx tb = new SecureTextBoxEx();
			SecureTextBoxEx.InitEx(ref tb);
			tb.Font = CerberusTheme.UiFont(12f, FontStyle.Regular);
			tb.Width = ContentWidth - 50;
			tb.Margin = new Padding(0, 2, 4, 0);
			tb.EnableProtection(true);

			CheckBox eye = new CheckBox { Appearance = Appearance.Button, Text = "",
				Font = new Font("Segoe MDL2 Assets", 11f), Size = new Size(40, tb.Height + 2),
				TextAlign = ContentAlignment.MiddleCenter, Margin = new Padding(0, 1, 0, 0) };
			new ToolTip().SetToolTip(eye, "Afficher / masquer");
			eye.CheckedChanged += delegate { tb.EnableProtection(!eye.Checked); };

			t.Controls.Add(tb, 0, 0);
			t.Controls.Add(eye, 1, 0);
			m_body.Controls.Add(t);
			cbEye = eye;
			return tb;
		}

		protected CheckBox AddOption(string strText, bool bChecked)
		{
			CheckBox cb = new CheckBox { Text = strText, AutoSize = true, Checked = bChecked,
				Margin = new Padding(0, 6, 0, 2) };
			m_body.Controls.Add(cb);
			return cb;
		}

		protected TextBox AddFileRow(string strInitial, params Button[] vButtons)
		{
			FlowLayoutPanel f = new FlowLayoutPanel { AutoSize = true, WrapContents = false,
				Margin = new Padding(22, 0, 0, 2) };
			int wBtn = 0;
			foreach(Button b in vButtons) wBtn += b.Width + 6;
			TextBox tb = new TextBox { Text = (strInitial ?? string.Empty),
				Width = ContentWidth - 22 - wBtn, Margin = new Padding(0, 3, 6, 0) };
			f.Controls.Add(tb);
			foreach(Button b in vButtons) { b.Margin = new Padding(0, 2, 6, 0); f.Controls.Add(b); }
			m_body.Controls.Add(f);
			return tb;
		}

		protected static Button NewButton(string strText)
		{
			Button b = new Button { Text = strText, AutoSize = true, MinimumSize = new Size(96, 30),
				Padding = new Padding(8, 0, 8, 0) };
			return b;
		}

		protected static void MakePrimary(Button b)
		{
			b.FlatStyle = FlatStyle.Flat;
			b.BackColor = CerberusTheme.Accent;
			b.ForeColor = Color.White;
			b.FlatAppearance.BorderColor = CerberusTheme.Accent;
			b.FlatAppearance.MouseOverBackColor = Color.FromArgb(90, 160, 255);
			b.FlatAppearance.MouseDownBackColor = Color.FromArgb(40, 110, 220);
			b.Font = CerberusTheme.UiFont(9.75f, FontStyle.Bold);
		}

		protected static string BrowseKeyFile(IWin32Window owner, string strCurrent)
		{
			using(OpenFileDialog ofd = new OpenFileDialog())
			{
				ofd.Title = "Choisir le fichier clé";
				ofd.Filter = "Fichiers clés (*.keyx;*.key)|*.keyx;*.key|Tous les fichiers (*.*)|*.*";
				ofd.CheckFileExists = true;
				if(!string.IsNullOrEmpty(strCurrent))
				{
					try { ofd.InitialDirectory = Path.GetDirectoryName(strCurrent); } catch(Exception) { }
				}
				return ((ofd.ShowDialog(owner) == DialogResult.OK) ? ofd.FileName : null);
			}
		}

		protected static string DbName(IOConnectionInfo ioc)
		{
			if(ioc == null) return string.Empty;
			string s = ioc.Path;
			return (string.IsNullOrEmpty(s) ? string.Empty : s);
		}

		/// <summary>Build the composite key (same order as KeePass).</summary>
		protected static CompositeKey BuildKey(byte[] pbPasswordUtf8, string strKeyFile,
			bool bCerberus, bool bUserAccount, IOConnectionInfo ioc, bool bNewKey, bool bSecDesk)
		{
			CompositeKey ck = new CompositeKey();
			if(pbPasswordUtf8 != null)
				ck.AddUserKey(new KcpPassword(pbPasswordUtf8,
					Program.Config.Security.MasterPassword.RememberWhileOpen));

			if(!string.IsNullOrEmpty(strKeyFile))
			{
				try { ck.AddUserKey(new KcpKeyFile(strKeyFile, bNewKey)); }
				catch(Exception ex)
				{
					MessageService.ShowWarning(strKeyFile, ex);
					return null;
				}
			}

			if(bCerberus)
			{
				KeyProvider kp = Program.KeyProviderPool.Get(CerberusKeyProvider.ProviderName);
				if(kp == null) { MessageService.ShowWarning("Fournisseur Cerberus introuvable."); return null; }
				if(bSecDesk)
				{
					MessageService.ShowWarning("Les facteurs Cerberus ne sont pas disponibles sur le bureau sécurisé." +
						MessageService.NewParagraph + "Désactivez « Saisir la clé maître sur un bureau sécurisé » dans les options.");
					return null;
				}
				byte[] pbKey = kp.GetKey(new KeyProviderQueryContext(ioc, bNewKey, bSecDesk));
				if((pbKey == null) || (pbKey.Length == 0)) return null;
				try { ck.AddUserKey(new KcpCustomKey(CerberusKeyProvider.ProviderName, pbKey, !kp.DirectKey)); }
				finally { MemUtil.ZeroByteArray(pbKey); }
			}

			if(bUserAccount)
			{
				try { ck.AddUserKey(new KcpUserAccount()); }
				catch(Exception ex) { MessageService.ShowWarning(ex); return null; }
			}

			return ((ck.UserKeyCount != 0) ? ck : null);
		}
	}

	/// <summary>Unlock dialog.</summary>
	internal sealed class CerberusKeyPromptForm : CerberusDialog
	{
		private readonly IOConnectionInfo m_ioc;
		private readonly bool m_bCanExit;
		private SecureTextBoxEx m_tbPw;
		private TextBox m_tbKeyFile;
		private CheckBox m_cbKeyFile, m_cbCerberus, m_cbAccount;
		private Label m_lblCaps;
		private Button m_btnOK, m_btnCancel, m_btnExit;

		public bool SecureDesktopMode = false;
		public CompositeKey CompositeKey = null;
		public bool HasClosedWithExit = false;

		public CerberusKeyPromptForm(IOConnectionInfo ioc, bool bCanExit, string strCustomTitle)
			: base(string.IsNullOrEmpty(strCustomTitle) ? "Déverrouiller la base" : strCustomTitle, DbName(ioc))
		{
			m_ioc = (ioc ?? new IOConnectionInfo());
			m_bCanExit = bCanExit;

			AceKeyAssoc a = Program.Config.Defaults.GetKeySources(m_ioc);
			string strKf = ((a != null) ? a.KeyFilePath : null);
			bool bCerb = ((a != null) && (a.KeyProvider == CerberusKeyProvider.ProviderName));
			bool bAcc = ((a != null) && a.UserAccount);

			AddHeading("Mot de passe maître");
			CheckBox cbEye;
			m_tbPw = AddPasswordRow(out cbEye);
			m_lblCaps = AddNote("⚠ Verr. Maj activé");
			m_lblCaps.ForeColor = Color.FromArgb(255, 170, 60);
			m_lblCaps.Margin = new Padding(0, 0, 0, 2);
			m_lblCaps.Visible = false;
			m_tbPw.KeyUp += delegate { UpdateCaps(); };
			m_tbPw.Enter += delegate { UpdateCaps(); };

			AddHeading("Autres éléments de la clé");
			m_cbKeyFile = AddOption("Fichier clé", !string.IsNullOrEmpty(strKf));
			Button btnBrowse = NewButton("Parcourir…");
			m_tbKeyFile = AddFileRow(strKf, btnBrowse);
			btnBrowse.Click += delegate
			{
				string s = BrowseKeyFile(this, m_tbKeyFile.Text);
				if(s != null) { m_tbKeyFile.Text = s; m_cbKeyFile.Checked = true; }
			};
			m_tbKeyFile.TextChanged += delegate { if(m_tbKeyFile.Text.Length > 0) m_cbKeyFile.Checked = true; };

			m_cbCerberus = AddOption("Facteurs Cerberus (PIN / motif / Shamir)", bCerb);
			m_cbAccount = AddOption("Compte utilisateur Windows", bAcc);
			if(NativeLib.IsUnix()) { m_cbAccount.Checked = false; m_cbAccount.Enabled = false; }

			AddNote("Mot de passe vide + aucun autre élément = clé avec mot de passe vide.");

			m_btnOK = NewButton("Déverrouiller");
			m_btnCancel = NewButton("Annuler");
			m_btnOK.Click += this.OnOK;
			m_btnCancel.DialogResult = DialogResult.Cancel;
			m_buttons.Controls.Add(m_btnOK);
			m_buttons.Controls.Add(m_btnCancel);
			if(m_bCanExit)
			{
				m_btnExit = NewButton("Quitter");
				m_btnExit.Click += delegate { HasClosedWithExit = true; this.DialogResult = DialogResult.Abort; Close(); };
				m_buttons.Controls.Add(m_btnExit);
			}
			this.AcceptButton = m_btnOK;
			this.CancelButton = m_btnCancel;
			this.Shown += delegate { m_tbPw.Focus(); UpdateCaps(); };
		}

		protected override void StyleButtons() { MakePrimary(m_btnOK); }

		private void UpdateCaps()
		{
			m_lblCaps.Visible = Control.IsKeyLocked(Keys.CapsLock);
		}

		private void OnOK(object sender, EventArgs e)
		{
			byte[] pb = m_tbPw.TextEx.ReadUtf8();
			try
			{
				bool bKf = m_cbKeyFile.Checked && (m_tbKeyFile.Text.Trim().Length > 0);
				bool bOther = bKf || m_cbCerberus.Checked || m_cbAccount.Checked;
				byte[] pbUse = (((pb.Length > 0) || !bOther) ? pb : null);

				CompositeKey ck = BuildKey(pbUse, (bKf ? m_tbKeyFile.Text.Trim() : null),
					m_cbCerberus.Checked, m_cbAccount.Checked, m_ioc, false, SecureDesktopMode);
				if(ck == null) return;

				this.CompositeKey = ck;
				this.DialogResult = DialogResult.OK;
				Close();
			}
			finally { MemUtil.ZeroByteArray(pb); }
		}
	}

	/// <summary>New master key dialog.</summary>
	internal sealed class CerberusKeyCreationForm : CerberusDialog
	{
		private readonly IOConnectionInfo m_ioc;
		private SecureTextBoxEx m_tbPw, m_tbPw2;
		private ProgressBar m_pbQuality;
		private Label m_lblQuality;
		private TextBox m_tbKeyFile;
		private CheckBox m_cbPassword, m_cbKeyFile, m_cbCerberus, m_cbAccount;
		private Button m_btnOK, m_btnCancel;

		public bool SecureDesktopMode = false;
		public CompositeKey CompositeKey = null;

		public CerberusKeyCreationForm(IOConnectionInfo ioc, bool bCreatingNew)
			: base(bCreatingNew ? "Protéger la nouvelle base" : "Changer la clé maître", DbName(ioc))
		{
			m_ioc = (ioc ?? new IOConnectionInfo());

			AddNote("Choisissez un ou plusieurs éléments. TOUS seront exigés à chaque ouverture.").Margin =
				new Padding(0, 0, 0, 2);

			m_cbPassword = AddOption("Mot de passe maître", true);
			m_cbPassword.Font = CerberusTheme.UiFont(9.75f, FontStyle.Bold);
			CheckBox cbEye;
			m_tbPw = AddPasswordRow(out cbEye);
			Label lRep = AddNote("Confirmation");
			lRep.Margin = new Padding(0, 2, 0, 0);
			CheckBox cbEye2;
			m_tbPw2 = AddPasswordRow(out cbEye2);
			cbEye.CheckedChanged += delegate
			{
				cbEye2.Checked = cbEye.Checked;
				m_tbPw2.Enabled = !cbEye.Checked && m_cbPassword.Checked; // shown = no need to repeat
			};
			cbEye2.Visible = false;

			FlowLayoutPanel fq = new FlowLayoutPanel { AutoSize = true, WrapContents = false,
				Margin = new Padding(0, 2, 0, 4) };
			m_pbQuality = new ProgressBar { Width = 300, Height = 8, Maximum = 128,
				Style = ProgressBarStyle.Continuous, Margin = new Padding(0, 6, 10, 0) };
			m_lblQuality = new Label { AutoSize = true, Text = "0 bits", ForeColor = CerberusTheme.Muted };
			fq.Controls.Add(m_pbQuality); fq.Controls.Add(m_lblQuality);
			m_body.Controls.Add(fq);
			m_tbPw.TextChanged += delegate { UpdateQuality(); };

			m_cbPassword.CheckedChanged += delegate
			{
				m_tbPw.Enabled = m_cbPassword.Checked;
				m_tbPw2.Enabled = m_cbPassword.Checked && !cbEye.Checked;
			};

			AddHeading("Éléments supplémentaires");
			m_cbKeyFile = AddOption("Fichier clé", false);
			Button btnNew = NewButton("Créer…");
			Button btnBrowse = NewButton("Parcourir…");
			m_tbKeyFile = AddFileRow(null, btnNew, btnBrowse);
			btnBrowse.Click += delegate
			{
				string s = BrowseKeyFile(this, m_tbKeyFile.Text);
				if(s != null) { m_tbKeyFile.Text = s; m_cbKeyFile.Checked = true; }
			};
			btnNew.Click += delegate
			{
				using(KeyFileCreationForm f = new KeyFileCreationForm())
				{
					f.InitEx(m_ioc);
					if((UIUtil.ShowDialogAndDestroy(f) == DialogResult.OK) && !string.IsNullOrEmpty(f.ResultFile))
					{ m_tbKeyFile.Text = f.ResultFile; m_cbKeyFile.Checked = true; }
				}
			};
			m_tbKeyFile.TextChanged += delegate { if(m_tbKeyFile.Text.Length > 0) m_cbKeyFile.Checked = true; };
			AddNote("Gardez une copie de sauvegarde du fichier clé : sans lui, la base est irrécupérable.");

			m_cbCerberus = AddOption("Facteurs Cerberus (PIN / motif / Shamir)", false);
			AddNote("PIN, motif sur grille et/ou parts Shamir K parmi N — configurés à l'étape suivante.");

			m_cbAccount = AddOption("Compte utilisateur Windows", false);
			AddNote("Lie la base à ce compte Windows : une réinstallation de Windows la rend illisible.");
			if(NativeLib.IsUnix()) m_cbAccount.Enabled = false;

			m_btnOK = NewButton(bCreatingNew ? "Créer" : "Enregistrer");
			m_btnCancel = NewButton("Annuler");
			m_btnOK.Click += this.OnOK;
			m_btnCancel.DialogResult = DialogResult.Cancel;
			m_buttons.Controls.Add(m_btnOK);
			m_buttons.Controls.Add(m_btnCancel);
			this.AcceptButton = m_btnOK;
			this.CancelButton = m_btnCancel;
			this.Shown += delegate { m_tbPw.Focus(); };
		}

		protected override void StyleButtons() { MakePrimary(m_btnOK); }

		private void UpdateQuality()
		{
			byte[] pb = m_tbPw.TextEx.ReadUtf8();
			uint uBits = QualityEstimation.EstimatePasswordBits(pb);
			MemUtil.ZeroByteArray(pb);
			m_pbQuality.Value = (int)Math.Min(uBits, 128U);
			m_lblQuality.Text = uBits.ToString() + " bits — " +
				((uBits < 50) ? "faible" : ((uBits < 80) ? "correct" : ((uBits < 110) ? "fort" : "très fort")));
		}

		private void OnOK(object sender, EventArgs e)
		{
			bool bKf = m_cbKeyFile.Checked && (m_tbKeyFile.Text.Trim().Length > 0);
			if(m_cbKeyFile.Checked && !bKf)
			{ MessageService.ShowWarning("Choisissez ou créez un fichier clé (ou décochez cette option)."); return; }
			if(!m_cbPassword.Checked && !bKf && !m_cbCerberus.Checked && !m_cbAccount.Checked)
			{ MessageService.ShowWarning("Choisissez au moins un élément de clé."); return; }

			byte[] pb = null;
			try
			{
				if(m_cbPassword.Checked)
				{
					pb = m_tbPw.TextEx.ReadUtf8();
					if(m_tbPw2.Enabled)
					{
						byte[] pb2 = m_tbPw2.TextEx.ReadUtf8();
						bool bEq = MemUtil.ArraysEqual(pb, pb2);
						MemUtil.ZeroByteArray(pb2);
						if(!bEq) { MessageService.ShowWarning("Les deux mots de passe ne correspondent pas."); return; }
					}
					string strErr = KeyUtil.ValidateNewMasterPassword(pb, (uint)m_tbPw.TextLength);
					if(strErr != null)
					{
						if(strErr.Length != 0) MessageService.ShowWarning(strErr);
						return;
					}
				}

				CompositeKey ck = BuildKey(pb, (bKf ? m_tbKeyFile.Text.Trim() : null),
					m_cbCerberus.Checked, m_cbAccount.Checked, m_ioc, true, SecureDesktopMode);
				if(ck == null) return;

				this.CompositeKey = ck;
				this.DialogResult = DialogResult.OK;
				Close();
			}
			finally { if(pb != null) MemUtil.ZeroByteArray(pb); }
		}
	}
}
