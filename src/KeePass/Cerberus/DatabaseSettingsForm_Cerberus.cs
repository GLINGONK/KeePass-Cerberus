/*
  KeePass Cerberus - one-click security profiles in Database Settings,
  matching the original Cerberus profiles.
  Licensed under the GNU GPL v2 or later (same as KeePass).
*/

using System;
using System.Drawing;
using System.Windows.Forms;

using KeePass.Cerberus;

using KeePassLib;
using KeePassLib.Cryptography.KeyDerivation;
using KeePassLib.Utility;

namespace KeePass.Forms
{
	public partial class DatabaseSettingsForm
	{
		private void CerberusAttachProfiles()
		{
			Button btn = new Button();
			btn.Text = "Profil Cerberus ▾";
			btn.AutoSize = true;
			btn.Font = new Font(this.Font, FontStyle.Bold);
			btn.Location = new Point(m_grpEncAlgo.Right - 150, m_lblSecIntro.Top - 5);
			m_tabSecurity.Controls.Add(btn);
			btn.BringToFront();
			btn.Left = m_grpEncAlgo.Right - btn.Width;

			ContextMenuStrip cms = new ContextMenuStrip();
			AddProfile(cms, "Interactif — AES-256 → ChaCha20, Argon2id 256 Mio (~0,5 s)",
				CerberusCiphers.Recommended, 256UL, 3, 4);
			AddProfile(cms, "Renforcé (recommandé) — AES-256 → ChaCha20, Argon2id 1 Gio (~2 s)",
				CerberusCiphers.Recommended, 1024UL, 4, 4);
			AddProfile(cms, "Paranoïa — AES-256 → ChaCha20 → Serpent-256, Argon2id 4 Gio (lent)",
				CerberusCiphers.Paranoid, 4096UL, 8, 8);
			CerberusTheme.ApplyContextMenu(cms);
			btn.Click += delegate { cms.Show(btn, new Point(0, btn.Height)); };
		}

		private void AddProfile(ContextMenuStrip cms, string strText, CascadeLayer[] vLayers,
			ulong uMemMiB, ulong uIt, uint uPar)
		{
			ToolStripMenuItem tsmi = new ToolStripMenuItem(strText);
			tsmi.Click += delegate
			{
				if(uMemMiB >= 4096UL)
				{
					if(!MessageService.AskYesNo("Le profil Paranoïa utilise 4 Gio de mémoire et " +
						"plusieurs secondes de calcul (voire plus) à chaque ouverture et enregistrement." +
						MessageService.NewParagraph + "Il faut au moins 6 Go de RAM libre. " +
						"Une base protégée ainsi ne s'ouvre que dans KeePass Cerberus." +
						MessageService.NewParagraph + "Appliquer ce profil ?"))
						return;
				}

				PwUuid pu = new CascadeCipherEngine(vLayers).CipherUuid;
				int iCipher = KeePassLib.Cryptography.Cipher.CipherPool.GlobalPool.GetCipherIndex(pu);
				if(iCipher >= 0) m_cmbEncAlgo.SelectedIndex = iCipher;

				KdfParameters p = new Argon2Kdf(Argon2Type.ID).GetDefaultParameters();
				p.SetUInt64(Argon2Kdf.ParamMemory, uMemMiB * 1024UL * 1024UL);
				p.SetUInt64(Argon2Kdf.ParamIterations, uIt);
				p.SetUInt32(Argon2Kdf.ParamParallelism, uPar);
				SetKdfParameters(p);
			};
			cms.Items.Add(tsmi);
		}
	}
}
