﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Chummer.Skills;

namespace Chummer.UI.Skills
{
	[DebuggerDisplay("{_skill.Name} {Visible} {btnAddSpec.Visible}")]
	public partial class SkillControl2 : UserControl
	{
		private readonly Skill _skill;
		private readonly Font _normal;
		private readonly Font _italic;
		private CharacterAttrib _attributeActive;

		public SkillControl2(Skill skill)
		{
			this._skill = skill;
			InitializeComponent();

			DataBindings.Add("Enabled", skill, nameof(Skill.Enabled), false, DataSourceUpdateMode.OnPropertyChanged);

			//Display
			if (!skill.Default)
			{
				lblName.Font = new Font(lblName.Font, FontStyle.Italic);
			}
			if (!String.IsNullOrWhiteSpace(_skill.Notes))
			{
				lblName.ForeColor = Color.SaddleBrown;
			}

			lblName.DataBindings.Add("Text", skill, nameof(Skill.DisplayName));

			skill.PropertyChanged += Skill_PropertyChanged;

			_attributeActive = skill.AttributeObject;
			_attributeActive.PropertyChanged += AttributeActiveOnPropertyChanged;
			Skill_PropertyChanged(null, null);  //if null it updates all
			
			
			_normal = btnAttribute.Font;
			_italic = new Font(_normal, FontStyle.Italic);
			if (skill.CharacterObject.Created)
			{
				lblModifiedRating.Location = new Point(256 - 13, 4);

				lblCareerRating.DataBindings.Add("Text", skill, nameof(Skill.Rating), false,
					DataSourceUpdateMode.OnPropertyChanged);
				lblCareerRating.Visible = true;

				btnCareerIncrease.Visible = true;
				btnCareerIncrease.DataBindings.Add("Enabled", skill, nameof(Skill.CanUpgradeCareer), false,
					DataSourceUpdateMode.OnPropertyChanged);
				nudSkill.Visible = false;
				nudKarma.Visible = false;
				chkKarma.Visible = false;

				cboSpec.Visible = false;


				lblCareerSpec.DataBindings.Add("Text", skill, nameof(skill.DisplaySpecialization), false, DataSourceUpdateMode.OnPropertyChanged);
				lblCareerSpec.Visible = true;

				lblAttribute.Visible = false;  //Was true, cannot think it should be

				btnAttribute.DataBindings.Add("Text", skill, nameof(Skill.DisplayAttribute));
				btnAttribute.Visible = true;

				SetupDropdown();
			}
			else
			{
				lblAttribute.DataBindings.Add("Text", skill, nameof(Skill.DisplayAttribute));
				
				//Up down boxes
				nudKarma.DataBindings.Add("Value", skill, nameof(Skill.Karma), false, DataSourceUpdateMode.OnPropertyChanged);
				nudSkill.DataBindings.Add("Value", skill, nameof(Skill.Base), false, DataSourceUpdateMode.OnPropertyChanged);

				nudSkill.DataBindings.Add("Enabled", skill, nameof(Skill.BaseUnlocked), false,
					DataSourceUpdateMode.OnPropertyChanged);
				nudKarma.DataBindings.Add("Enabled", skill, nameof(Skill.KarmaUnlocked), false,
					DataSourceUpdateMode.OnPropertyChanged);
				
				if (skill.CharacterObject.BuildMethod.HaveSkillPoints())
				{
					chkKarma.DataBindings.Add("Checked", skill, nameof(Skill.BuyWithKarma), false,
						DataSourceUpdateMode.OnPropertyChanged);
					chkKarma.DataBindings.Add("Enabled", skill, nameof(Skill.CanHaveSpecs), false, DataSourceUpdateMode.OnPropertyChanged);
				}
				else
				{
					chkKarma.Visible = false;
				}

				
				if (skill.IsExoticSkill)
				{
					cboSpec.Enabled = false;
					cboSpec.DataBindings.Add("Text", skill, nameof(Skill.DisplaySpecialization), false, DataSourceUpdateMode.OnPropertyChanged);

				}
				else
				{
					//dropdown/spec
					cboSpec.DataSource = skill.CGLSpecializations;
					cboSpec.DisplayMember = nameof(ListItem.Name);
					cboSpec.ValueMember = nameof(ListItem.Value);
					cboSpec.DataBindings.Add("Enabled", skill, nameof(Skill.CanHaveSpecs), false,
						DataSourceUpdateMode.OnPropertyChanged);
					cboSpec.SelectedIndex = -1;

					cboSpec.DataBindings.Add("Text", skill, nameof(Skill.Specialization), false, DataSourceUpdateMode.OnPropertyChanged);

				}
			}

			//Delete button
			if (skill.AllowDelete)
			{
				cmdDelete.Visible = true;
				cmdDelete.Click += (sender, args) => { skill.CharacterObject.SkillsSection.Skills.Remove(skill); };

				if (skill.CharacterObject.Created)
				{
					btnAddSpec.Location = new Point(btnAddSpec.Location.X - cmdDelete.Width, btnAddSpec.Location.Y); 
				}
			}
		}

		private void ContextMenu_Opening(object sender, CancelEventArgs e)
		{
			foreach (ToolStripItem objItem in ((ContextMenuStrip)sender).Items)
			{
				if (objItem.Tag != null)
					objItem.Text = LanguageManager.Instance.GetString(objItem.Tag.ToString());
			}
		}

		private void Skill_PropertyChanged(object sender, PropertyChangedEventArgs propertyChangedEventArgs)
		{
			//I learned something from this but i'm not sure it is a good solution
			//scratch that, i'm sure it is a bad solution. (Tooltip manager from tooltip, properties from reflection?

			//if name of changed is null it does magic to change all, otherwise it only does one.
			bool all = false;
			switch (propertyChangedEventArgs?.PropertyName)
			{
				case null:
					all = true;
					goto case nameof(Skill.Leveled);

				case nameof(Skill.DisplayPool):
					all = true;
					goto case nameof(Skill.PoolToolTip);

				case nameof(Skill.Leveled):
					BackColor = _skill.Leveled ? SystemColors.ButtonHighlight : SystemColors.Control;
					btnAddSpec.Visible = _skill.CharacterObject.Created && _skill.Leveled &&  !_skill.IsExoticSkill;
					if (all) { goto case nameof(Skill.SkillToolTip); }  break;


				case nameof(Skill.SkillToolTip):
					tipTooltip.SetToolTip(lblName, _skill.SkillToolTip);  //is this the best way?
					//tipTooltip.SetToolTip(this, skill.SkillToolTip);
					//tipTooltip.SetToolTip(lblAttribute, skill.SkillToolTip);
					//tipTooltip.SetToolTip(lblCareerSpec, skill.SkillToolTip);
					if (all) { goto case nameof(Skill.AddSpecToolTip); } break;


				case nameof(Skill.AddSpecToolTip):
					tipTooltip.SetToolTip(btnAddSpec, _skill.AddSpecToolTip);
					if (all) { goto case nameof(Skill.PoolToolTip); } break;


				case nameof(Skill.PoolToolTip):
					tipTooltip.SetToolTip(lblModifiedRating, _skill.PoolToolTip);
					if (all) { goto case nameof(Skill.UpgradeToolTip); } break;


				case nameof(Skill.UpgradeToolTip):
					tipTooltip.SetToolTip(btnCareerIncrease, _skill.UpgradeToolTip);
					if (all) { goto case nameof(Skill.Rating); } break;

				case nameof(Skill.Rating):
                case nameof(Skill.Specialization):
					lblModifiedRating.Text =
						_skill.DisplayOtherAttribue(_attributeActive.TotalValue);
					break;
			}
		}
		
		private void btnCareerIncrease_Click(object sender, EventArgs e)
		{
			frmCareer parrent = ParentForm as frmCareer;
			if (parrent != null)
			{
				string confirmstring = string.Format(LanguageManager.Instance.GetString("Message_ConfirmKarmaExpense"),
					_skill.DisplayName, _skill.Rating + 1, _skill.UpgradeKarmaCost());
					
				if (!parrent.ConfirmKarmaExpense(confirmstring))
					return;
			}

			_skill.Upgrade();
		}

		private void btnAddSpec_Click(object sender, EventArgs e)
		{
			frmCareer parrent = ParentForm as frmCareer;
			if (parrent != null)
			{
				string confirmstring = string.Format(LanguageManager.Instance.GetString("Message_ConfirmKarmaExpenseSkillSpecialization"),
						_skill.CharacterObject.Options.KarmaSpecialization);

				if (!parrent.ConfirmKarmaExpense(confirmstring))
					return;
			}

			frmSelectSpec selectForm = new frmSelectSpec(_skill);
			selectForm.ShowDialog();

			if (selectForm.DialogResult != DialogResult.OK) return;

			_skill.AddSpecialization(selectForm.SelectedItem);

			//TODO turn this into a databinding, but i don't care enough right now
			lblCareerSpec.Text = string.Join(", ",
					(from specialization in _skill.Specializations
					 select specialization.DisplayName));

			parrent?.UpdateCharacterInfo();
		}

		private void SetupDropdown()
		{
			List<ListItem> list =  new[] {"BOD", "AGI", "REA", "STR", "CHA", "INT", "LOG", "WIL", "MAG", "RES"}.Select(
				x => new ListItem(x, LanguageManager.Instance.GetString($"String_Attribute{x}Short"))).ToList();

			cboSelectAttribute.ValueMember = "Value";
			cboSelectAttribute.DisplayMember = "Name";
			cboSelectAttribute.DataSource = list;
			cboSelectAttribute.SelectedValue = _skill.AttributeObject.Abbrev;
		}

		private void btnAttribute_Click(object sender, EventArgs e)
		{
			btnAttribute.Visible = false;
			cboSelectAttribute.Visible = true;
			cboSelectAttribute.DroppedDown = true;
		}

		private void cboSelectAttribute_Closed(object sender, EventArgs e)
		{
			btnAttribute.Visible = true;
			cboSelectAttribute.Visible = false;
			_attributeActive.PropertyChanged -= AttributeActiveOnPropertyChanged;
			_attributeActive = _skill.CharacterObject.GetAttribute((string) cboSelectAttribute.SelectedValue);

			btnAttribute.Font = _attributeActive == _skill.AttributeObject ? _normal : _italic;
			btnAttribute.Text = cboSelectAttribute.Text;

			_attributeActive.PropertyChanged += AttributeActiveOnPropertyChanged;
			AttributeActiveOnPropertyChanged(null, null);

			CustomAttributeChanged?.Invoke(this, EventArgs.Empty);
		}

		public event EventHandler CustomAttributeChanged;

		public bool CustomAttributeSet => _attributeActive != _skill.AttributeObject;

		public void ResetSelectAttribute()
		{
			if (CustomAttributeSet)
			{
				cboSelectAttribute.SelectedValue = _skill.AttributeObject.Abbrev;
				cboSelectAttribute_Closed(null, null);
			}
		}

		private void tsSkillLabelNotes_Click(object sender, EventArgs e)
		{
			try
			{
				frmNotes frmItemNotes = new frmNotes();
				frmItemNotes.Notes = _skill.Notes;
				frmItemNotes.ShowDialog(this);

				if (frmItemNotes.DialogResult == DialogResult.OK)
				{
					_skill.Notes = frmItemNotes.Notes;
					_skill.Notes = CommonFunctions.WordWrap(_skill.Notes, 100);
					tipTooltip.SetToolTip(lblName, _skill.SkillToolTip);
				}
				if (_skill.Notes != string.Empty)
				{
					lblName.ForeColor = Color.SaddleBrown;
				}
				else
				{
					lblName.ForeColor = Color.Black;
				}
			}
			catch
			{
			}
		}

		private void AttributeActiveOnPropertyChanged(object sender, PropertyChangedEventArgs propertyChangedEventArgs)
		{
			Skill_PropertyChanged(null, new PropertyChangedEventArgs(nameof(Skill.Rating)));
		}

        private void lblName_Click(object sender, EventArgs e)
        {
            CommonFunctions objCommon = new CommonFunctions(_skill.CharacterObject);
            objCommon.OpenPDF(_skill.Source+" "+_skill.Page);
        }
    }
}
