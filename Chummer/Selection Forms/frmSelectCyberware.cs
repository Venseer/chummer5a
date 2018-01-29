/*  This file is part of Chummer5a.
 *
 *  Chummer5a is free software: you can redistribute it and/or modify
 *  it under the terms of the GNU General Public License as published by
 *  the Free Software Foundation, either version 3 of the License, or
 *  (at your option) any later version.
 *
 *  Chummer5a is distributed in the hope that it will be useful,
 *  but WITHOUT ANY WARRANTY; without even the implied warranty of
 *  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 *  GNU General Public License for more details.
 *
 *  You should have received a copy of the GNU General Public License
 *  along with Chummer5a.  If not, see <http://www.gnu.org/licenses/>.
 *
 *  You can obtain the full source code for Chummer5a at
 *  https://github.com/chummer5a/chummer5a
 */
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using System.Xml;
using System.Xml.XPath;
using Chummer.Backend.Equipment;
using Chummer.Backend.Attributes;
using System.Text;
using Chummer.Backend;

namespace Chummer
{
    public partial class frmSelectCyberware : Form
    {
        private readonly Character _objCharacter;
        private List<Grade> _lstGrades;
        private readonly string _strNoneGradeId;

        private decimal _decCostMultiplier = 1.0m;
        private decimal _decESSMultiplier = 1.0m;
        private int _intAvailModifier;

        private Grade _objForcedGrade = null;
        private string _strSubsystems = string.Empty;
        private string _strDisallowedMounts = string.Empty;
        private string _strHasModularMounts = string.Empty;
        private decimal _decMaximumCapacity = -1;
        private bool _blnLockGrade;
        private bool _blnLoading = true;

        private readonly Mode _objMode = Mode.Cyberware;
        private string _strNodeXPath = "/chummer/cyberwares/cyberware";
        private static string _sStrSelectCategory = string.Empty;
        private static string _sStrSelectGrade = string.Empty;
        private string _strSelectedCategory = string.Empty;
        private string _strOldSelectedGrade = string.Empty;
        private bool _blnOldGradeEnabled = true;
        private bool _blnIgnoreSecondHand = false;
        private string _strForceGrade = string.Empty;
        private readonly XmlNode _objParentNode = null;
        private readonly HashSet<string> _setBlackMarketMaps;
        private readonly XmlDocument _objXmlDocument = null;

        private enum Mode
        {
            Cyberware = 0,
            Bioware,
        }

        #region Control Events
        public frmSelectCyberware(Character objCharacter, Improvement.ImprovementSource objWareSource, XmlNode objParentNode = null)
        {
            InitializeComponent();
            
            _objCharacter = objCharacter;
            _objParentNode = objParentNode;

            switch (objWareSource)
            {
                case Improvement.ImprovementSource.Cyberware:
                    _objMode = Mode.Cyberware;
                    _objXmlDocument = XmlManager.Load("cyberware.xml");
                    _strNodeXPath = "/chummer/cyberwares/cyberware";
                    Tag = "Title_SelectCyberware";
                    break;
                case Improvement.ImprovementSource.Bioware:
                    _objMode = Mode.Bioware;
                    _objXmlDocument = XmlManager.Load("bioware.xml");
                    _strNodeXPath = "/chummer/biowares/bioware";
                    Tag = "Title_SelectCyberware_Bioware";
                    break;
            }

            LanguageManager.TranslateWinForm(GlobalOptions.Language, this);
            MoveControls();

            _lstGrades = (List<Grade>)_objCharacter.GetGradeList(objWareSource);
            _strNoneGradeId = _lstGrades.FirstOrDefault(x => x.Name == "None").SourceId.ToString("D");
            _setBlackMarketMaps = _objCharacter.GenerateBlackMarketMappings(_objXmlDocument);
        }

        private void frmSelectCyberware_Load(object sender, EventArgs e)
        {
            if (_objCharacter.Created)
            {
                lblMarkupLabel.Visible = true;
                nudMarkup.Visible = true;
                lblMarkupPercentLabel.Visible = true;
                chkHideBannedGrades.Visible = false;
                chkHideOverAvailLimit.Visible = false;
                chkHideOverAvailLimit.Checked = false;
            }
            else
            {
                lblMarkupLabel.Visible = false;
                nudMarkup.Visible = false;
                lblMarkupPercentLabel.Visible = false;
                chkHideBannedGrades.Visible = true;
                chkHideOverAvailLimit.Text = chkHideOverAvailLimit.Text.Replace("{0}", _objCharacter.MaximumAvailability.ToString());
                chkHideOverAvailLimit.Checked = _objCharacter.Options.HideItemsOverAvailLimit;
            }

            chkPrototypeTranshuman.Visible = _objCharacter.PrototypeTranshuman > 0 && _objMode == Mode.Bioware && !_objCharacter.Created;

            PopulateCategories();
            // Select the first Category in the list.
            if (!string.IsNullOrEmpty(_sStrSelectCategory))
                cboCategory.SelectedValue = _sStrSelectCategory;
            if (cboCategory.SelectedIndex == -1 && cboCategory.Items.Count > 0)
                cboCategory.SelectedIndex = 0;
            _strSelectedCategory = cboCategory.SelectedValue?.ToString();

            chkBlackMarketDiscount.Visible = _objCharacter.BlackMarketDiscount;

            // Populate the Grade list. Do not show the Adapsin Grades if Adapsin is not enabled for the character.
            PopulateGrades(false, true, _objForcedGrade?.SourceId.ToString("D") ?? string.Empty, chkHideBannedGrades.Checked);

            if (_objForcedGrade != null)
                cboGrade.SelectedValue = _objForcedGrade.SourceId.ToString();
            else if (!string.IsNullOrEmpty(_sStrSelectGrade))
                cboGrade.SelectedValue = _sStrSelectGrade;
            if (cboGrade.SelectedIndex == -1 && cboGrade.Items.Count > 0)
                cboGrade.SelectedIndex = 0;

            lblESSDiscountLabel.Visible = _objCharacter.Options.AllowCyberwareESSDiscounts;
            lblESSDiscountPercentLabel.Visible = _objCharacter.Options.AllowCyberwareESSDiscounts;
            nudESSDiscount.Visible = _objCharacter.Options.AllowCyberwareESSDiscounts;
            
            _blnLoading = false;
            RefreshList(_strSelectedCategory);
        }

        private void cboGrade_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_blnLoading)
                return;
            _blnLoading = true;

            XmlNode xmlGrade = null;
            // Retrieve the information for the selected Grade.
            string strSelectedGrade = cboGrade.SelectedValue?.ToString();
            if (cboGrade.Enabled && strSelectedGrade != null)
                _strOldSelectedGrade = strSelectedGrade;
            if (!string.IsNullOrEmpty(strSelectedGrade))
                xmlGrade = _objXmlDocument.SelectSingleNode("/chummer/grades/grade[id = \"" + strSelectedGrade + "\"]");

            // Update the Essence and Cost multipliers based on the Grade that has been selected.
            if (xmlGrade != null)
            {
                _decCostMultiplier = Convert.ToDecimal(xmlGrade["cost"]?.InnerText, GlobalOptions.InvariantCultureInfo);
                _decESSMultiplier = Convert.ToDecimal(xmlGrade["ess"]?.InnerText, GlobalOptions.InvariantCultureInfo);
                _intAvailModifier = Convert.ToInt32(xmlGrade["avail"]?.InnerText);

                PopulateCategories();
                _blnLoading = false;
                RefreshList(_strSelectedCategory);
            }
            else
            {
                _blnLoading = false;
                UpdateCyberwareInfo();
            }
        }
        private void cboGrade_EnabledChanged(object sender, EventArgs e)
        {
            if (cboGrade.Enabled != _blnOldGradeEnabled)
            {
                _blnOldGradeEnabled = cboGrade.Enabled;
                if (_blnOldGradeEnabled)
                {
                    cboGrade.SelectedValue = _strOldSelectedGrade;
                }
                cboGrade_SelectedIndexChanged(sender, e);
            }
        }

        private void cboCategory_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_blnLoading)
                return;
            _blnLoading = true;
            _strSelectedCategory = cboCategory.SelectedValue?.ToString();
            string strForceGrade = string.Empty;
            // Update the list of Cyberware based on the selected Category.
            cboGrade.Enabled = !_blnLockGrade;
            if (_blnLockGrade)
                strForceGrade = cboGrade.SelectedValue?.ToString();
            // We may need to rebuild the Grade list since Cultured Bioware is not allowed to select Standard (Second-Hand) as Grade and ForceGrades can change.
            Grade objForcedGrade = _objForcedGrade ?? (string.IsNullOrEmpty(strForceGrade) ? null : _lstGrades.FirstOrDefault(x => x.SourceId.ToString("D") == strForceGrade));
            PopulateGrades(!string.IsNullOrEmpty(_strSelectedCategory) && !cboGrade.Enabled && objForcedGrade?.SecondHand != true, false, strForceGrade, chkHideBannedGrades.Checked);
            _blnLoading = false;
            RefreshList(_strSelectedCategory);
        }

        private void lstCyberware_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_blnLoading)
                return;
            _blnLoading = true;
            XmlNode xmlCyberware = null;
            string strSelectedId = lstCyberware.SelectedValue?.ToString();
            if (!string.IsNullOrEmpty(strSelectedId))
            {
                // Retrieve the information for the selected piece of Cyberware.
                xmlCyberware = _objXmlDocument.SelectSingleNode(_strNodeXPath + "[id = \"" + strSelectedId + "\"]");
            }
            if (xmlCyberware != null)
            {
                // If the piece has a Rating value, enable the Rating control, otherwise, disable it and set its value to 0.
                if (xmlCyberware?["rating"] != null)
                {
                    nudRating.Enabled = true;

                    string strMinRating = xmlCyberware["minrating"]?.InnerText;
                    int intMinRating = 1;
                    // Not a simple integer, so we need to start mucking around with strings
                    if (!string.IsNullOrEmpty(strMinRating) && !int.TryParse(strMinRating, out intMinRating))
                    {
                        strMinRating = strMinRating.CheapReplace("MaximumSTR", () => (ParentVehicle != null ? Math.Max(1, ParentVehicle.TotalBody * 2) : _objCharacter.STR.TotalMaximum).ToString());
                        strMinRating = strMinRating.CheapReplace("MaximumAGI", () => (ParentVehicle != null ? Math.Max(1, ParentVehicle.Pilot * 2) : _objCharacter.AGI.TotalMaximum).ToString());
                        strMinRating = strMinRating.CheapReplace("MinimumSTR", () => (ParentVehicle?.TotalBody ?? 3).ToString());
                        strMinRating = strMinRating.CheapReplace("MinimumAGI", () => (ParentVehicle?.Pilot ?? 3).ToString());
                        try
                        {
                            intMinRating = Convert.ToInt32(CommonFunctions.EvaluateInvariantXPath(strMinRating));
                        }
                        catch (XPathException)
                        {
                            intMinRating = 1;
                        }
                    }
                    nudRating.Minimum = intMinRating;

                    string strMaxRating = xmlCyberware["rating"].InnerText;
                    int intMaxRating = 0;
                    // Not a simple integer, so we need to start mucking around with strings
                    if (!string.IsNullOrEmpty(strMaxRating) && !int.TryParse(strMaxRating, out intMaxRating))
                    {
                        strMaxRating = strMaxRating.CheapReplace("MaximumSTR", () => (ParentVehicle != null ? Math.Max(1, ParentVehicle.TotalBody * 2) : _objCharacter.STR.TotalMaximum).ToString());
                        strMaxRating = strMaxRating.CheapReplace("MaximumAGI", () => (ParentVehicle != null ? Math.Max(1, ParentVehicle.Pilot * 2) : _objCharacter.AGI.TotalMaximum).ToString());
                        strMaxRating = strMaxRating.CheapReplace("MinimumSTR", () => (ParentVehicle?.TotalBody ?? 3).ToString());
                        strMaxRating = strMaxRating.CheapReplace("MinimumAGI", () => (ParentVehicle?.Pilot ?? 3).ToString());
                        try
                        {
                            intMaxRating = Convert.ToInt32(CommonFunctions.EvaluateInvariantXPath(strMaxRating));
                        }
                        catch (XPathException)
                        {
                        }
                    }
                    nudRating.Maximum = intMaxRating;
                    if (chkHideOverAvailLimit.Checked)
                    {
                        int intAvailModifier = xmlCyberware["forcegrade"]?.InnerText == "None" ? 0 : _intAvailModifier;
                        while (nudRating.Maximum > intMinRating && !SelectionShared.CheckAvailRestriction(xmlCyberware, _objCharacter, decimal.ToInt32(nudRating.Maximum), intAvailModifier))
                        {
                            nudRating.Maximum -= 1;
                        }
                    }
                    nudRating.Value = nudRating.Minimum;
                }
                else
                {
                    nudRating.Minimum = 0;
                    nudRating.Value = 0;
                    nudRating.Enabled = false;
                }

                string strSource = xmlCyberware["source"].InnerText;
                string strPage = xmlCyberware["altpage"]?.InnerText ?? xmlCyberware["page"].InnerText;
                lblSource.Text = CommonFunctions.LanguageBookShort(strSource, GlobalOptions.Language) + ' ' + strPage;
                tipTooltip.SetToolTip(lblSource, CommonFunctions.LanguageBookLong(strSource, GlobalOptions.Language) + ' ' + LanguageManager.GetString("String_Page", GlobalOptions.Language) + ' ' + strPage);

                string strForceGrade = xmlCyberware["forcegrade"]?.InnerText;
                Grade objForcedGrade = null;
                if (!string.IsNullOrEmpty(strForceGrade))
                {
                    // Force the Cyberware to be a particular Grade.
                    if (cboGrade.Enabled)
                        cboGrade.Enabled = false;
                    objForcedGrade = _lstGrades.FirstOrDefault(x => x.Name == strForceGrade);
                    strForceGrade = objForcedGrade.SourceId.ToString("D");
                }
                else
                {
                    cboGrade.Enabled = !_blnLockGrade;
                    if (_blnLockGrade)
                    {
                        strForceGrade = _objForcedGrade?.SourceId.ToString("D") ?? cboGrade.SelectedValue?.ToString();
                        objForcedGrade = _objForcedGrade ?? _lstGrades.FirstOrDefault(x => x.SourceId.ToString("D") == strForceGrade);
                    }
                }

                chkBlackMarketDiscount.Enabled = true;
                chkBlackMarketDiscount.Checked = _setBlackMarketMaps.Contains(xmlCyberware["category"]?.InnerText);

                // We may need to rebuild the Grade list since Cultured Bioware is not allowed to select Standard (Second-Hand) as Grade and ForceGrades can change.
                PopulateGrades(xmlCyberware["nosecondhand"] != null || (!cboGrade.Enabled && objForcedGrade?.SecondHand != true), false, strForceGrade, chkHideBannedGrades.Checked);

                string strNotes = xmlCyberware["altnotes"]?.InnerText ?? xmlCyberware["notes"]?.InnerText;
                if (!string.IsNullOrEmpty(strNotes))
                {
                    lblCyberwareNotes.Visible = true;
                    lblCyberwareNotesLabel.Visible = true;
                    lblCyberwareNotes.Text = strNotes;
                }
                else
                {
                    lblCyberwareNotes.Visible = false;
                    lblCyberwareNotesLabel.Visible = false;
                }
            }
            else
            {
                nudRating.Minimum = 0;
                nudRating.Value = 0;
                nudRating.Enabled = false;
                cboGrade.Enabled = !_blnLockGrade;
                string strForceGrade = string.Empty;
                Grade objForcedGrade = null;
                if (_blnLockGrade)
                {
                    strForceGrade = _objForcedGrade?.SourceId.ToString("D") ?? cboGrade.SelectedValue?.ToString();
                    objForcedGrade = _objForcedGrade ?? _lstGrades.FirstOrDefault(x => x.SourceId.ToString("D") == strForceGrade);
                }
                PopulateGrades(_blnLockGrade && objForcedGrade?.SecondHand != true, false, strForceGrade, chkHideBannedGrades.Checked);
                chkBlackMarketDiscount.Enabled = false;
                chkBlackMarketDiscount.Checked = false;
                lblCyberwareNotes.Visible = false;
                lblCyberwareNotesLabel.Visible = false;
                lblSource.Text = string.Empty;
                tipTooltip.SetToolTip(lblSource, string.Empty);
            }
            _blnLoading = false;
            UpdateCyberwareInfo();
        }

        private void nudRating_ValueChanged(object sender, EventArgs e)
        {
            if (_blnLoading)
                return;
            UpdateCyberwareInfo();
        }

        private void chkHideOverAvailLimit_CheckedChanged(object sender, EventArgs e)
        {
            RefreshList(_strSelectedCategory);
        }

        private void nudMarkup_ValueChanged(object sender, EventArgs e)
        {
            if (_blnLoading)
                return;
            UpdateCyberwareInfo();
        }

        private void cmdOK_Click(object sender, EventArgs e)
        {
            AcceptForm();
        }

        private void cmdCancel_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
        }

        private void chkHideBannedGrades_CheckedChanged(object sender, EventArgs e)
        {
            if (_blnLoading)
                return;
            _lstGrades = (List<Grade>)_objCharacter.GetGradeList(_objMode == Mode.Bioware ? Improvement.ImprovementSource.Bioware : Improvement.ImprovementSource.Cyberware, chkHideBannedGrades.Checked);
            PopulateGrades(false, false, string.Empty, chkHideBannedGrades.Checked);
        }

        private void lstCyberware_DoubleClick(object sender, EventArgs e)
        {
            AcceptForm();
        }

        private void cmdOKAdd_Click(object sender, EventArgs e)
        {
            AddAgain = true;
            cmdOK_Click(sender, e);
        }

        private void txtSearch_TextChanged(object sender, EventArgs e)
        {
            RefreshList(_strSelectedCategory);
        }

        private void chkFree_CheckedChanged(object sender, EventArgs e)
        {
            if (_blnLoading)
                return;
            UpdateCyberwareInfo();
        }

        private void nudESSDiscount_ValueChanged(object sender, EventArgs e)
        {
            if (_blnLoading)
                return;
            UpdateCyberwareInfo();
        }

        private void chkBlackMarketDiscount_CheckedChanged(object sender, EventArgs e)
        {
            if (_blnLoading)
                return;
            UpdateCyberwareInfo();
        }

        private void txtSearch_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Down)
            {
                if (lstCyberware.SelectedIndex + 1 < lstCyberware.Items.Count)
                {
                    lstCyberware.SelectedIndex++;
                }
                else if (lstCyberware.Items.Count > 0)
                {
                    lstCyberware.SelectedIndex = 0;
                }
            }
            if (e.KeyCode == Keys.Up)
            {
                if (lstCyberware.SelectedIndex - 1 >= 0)
                {
                    lstCyberware.SelectedIndex--;
                }
                else if (lstCyberware.Items.Count > 0)
                {
                    lstCyberware.SelectedIndex = lstCyberware.Items.Count - 1;
                }
            }
        }

        private void txtSearch_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Up)
                txtSearch.Select(txtSearch.Text.Length, 0);
        }
        
        private void chkPrototypeTranshuman_CheckedChanged(object sender, EventArgs e)
        {
            if (_blnLoading)
                return;
            UpdateCyberwareInfo();
        }
        #endregion

        #region Properties
        /// <summary>
        /// Whether or not the user wants to add another item after this one.
        /// </summary>
        public bool AddAgain { get; private set; }

        /// <summary>
        /// Essence cost multiplier from the character.
        /// </summary>
        public decimal CharacterESSMultiplier { get; set; } = 1.0m;

        /// <summary>
        /// Total Essence cost multiplier from the character (stacks multiplicatively at the very last step.
        /// </summary>
        public decimal CharacterTotalESSMultiplier { get; set; } = 1.0m;

        /// <summary>
        /// Cost multiplier for Genetech.
        /// </summary>
        public decimal GenetechCostMultiplier { get; set; } = 1.0m;

        /// <summary>
        /// Essence cost multiplier for Basic Bioware.
        /// </summary>
        public decimal BasicBiowareESSMultiplier { get; set; } = 1.0m;
        
        /// <summary>
        /// Whether or not the item has no cost.
        /// </summary>
        public bool FreeCost => chkFree.Checked;

        /// <summary>
        /// Set the window's Mode to Cyberware or Bioware.
        /// </summary>
        private Mode WindowMode
        {
            get
            {
                return _objMode;
            }
        }

        /// <summary>
        /// Set the maximum Capacity the piece of Cyberware is allowed to be.
        /// </summary>
        public decimal MaximumCapacity
        {
            get { return _decMaximumCapacity; }
            set
            {
                _decMaximumCapacity = value;
                lblMaximumCapacity.Text = $"{LanguageManager.GetString("Label_MaximumCapacityAllowed", GlobalOptions.Language)} {_decMaximumCapacity:#,0.##}";
            }
        }

        /// <summary>
        /// Comma-separate list of Categories to show for Subsystems.
        /// </summary>
        public string Subsystems
        {
            set
            {
                _strSubsystems = value;
            }
        }

        /// <summary>
        /// Comma-separate list of mount locations that are disallowed.
        /// </summary>
        public string DisallowedMounts
        {
            set
            {
                _strDisallowedMounts = value;
            }
        }

        /// <summary>
        /// Comma-separate list of mount locations that already exist on the parent.
        /// </summary>
        public string HasModularMounts
        {
            set
            {
                _strHasModularMounts = value;
            }
        }

        /// <summary>
        /// Manually set the Grade of the piece of Cyberware.
        /// </summary>
        public Grade SetGrade
        {
            set => _objForcedGrade = value;
        }

        /// <summary>
        /// Name of Cyberware that was selected in the dialogue.
        /// </summary>
        public string SelectedCyberware { get; private set; } = string.Empty;

        /// <summary>
        /// Grade of the selected piece of Cyberware.
        /// </summary>
        public Grade SelectedGrade { get; private set; }

        /// <summary>
        /// Rating of the selected piece of Cyberware (0 if not applicable).
        /// </summary>
        public int SelectedRating { get; private set; }

        /// <summary>
        /// Selected Essence cost discount.
        /// </summary>
        public int SelectedESSDiscount { get; private set; }

        /// <summary>
        /// Whether or not the selected Vehicle is used.
        /// </summary>
        public bool BlackMarketDiscount { get; private set; }
        
        /// <summary>
        /// Parent vehicle that the cyberlimb will be attached to.
        /// </summary>
        public Vehicle ParentVehicle { set; get; }

        public decimal Markup { get; set; }

        /// <summary>
        /// Whether the bioware should be discounted by Prototype Transhuman.
        /// </summary>
        public bool PrototypeTranshuman => chkPrototypeTranshuman.Checked && _objMode == Mode.Bioware && !_objCharacter.Created;

        /// <summary>
        /// Parent cyberware that the current selection will be added to.
        /// </summary>
        public Cyberware CyberwareParent { get; set; }
        #endregion

        #region Methods
        /// <summary>
        /// Update the Cyberware's information based on the Cyberware selected and current Rating.
        /// </summary>
        private void UpdateCyberwareInfo()
        {
            XmlNode objXmlCyberware = null;
            string strSelectedId = lstCyberware.SelectedValue?.ToString();
            if (!string.IsNullOrEmpty(strSelectedId))
            {
                // Retireve the information for the selected piece of Cyberware.
                objXmlCyberware = _objXmlDocument.SelectSingleNode(_strNodeXPath + "[id = \"" + strSelectedId + "\"]");
            }
            if (objXmlCyberware == null)
            {
                lblCost.Text = string.Empty;
                lblAvail.Text = string.Empty;
                lblTest.Text = string.Empty;
                lblEssence.Text = string.Empty;
                lblCapacity.Text = string.Empty;
                return;
            }

            string strSelectCategory = objXmlCyberware["category"].InnerText;
            bool blnForceNoESSModifier = objXmlCyberware["forcegrade"]?.InnerText == "None";

            // Place the Genetech cost multiplier in a varaible that can be safely modified.
            decimal decGenetechCostModifier = 1;
            // Genetech cost modifier only applies to Genetech.
            if (strSelectCategory.StartsWith("Genetech") || strSelectCategory.StartsWith("Genetic Infusions") || strSelectCategory.StartsWith("Genemods"))
                decGenetechCostModifier = GenetechCostMultiplier;

            // Extract the Avil and Cost values from the Cyberware info since these may contain formulas and/or be based off of the Rating.
            // This is done using XPathExpression.

            int intRating = decimal.ToInt32(nudRating.Value);
            // Avail.
            // If avail contains "F" or "R", remove it from the string so we can use the expression.
            string strAvail = objXmlCyberware["avail"]?.InnerText;
            if (!string.IsNullOrEmpty(strAvail))
            {
                string strAvailExpr = strAvail;
                if (strAvailExpr.StartsWith("FixedValues("))
                {
                    string[] strValues = strAvailExpr.TrimStart("FixedValues(", true).TrimEnd(')').Split(',');
                    strAvailExpr = strValues[Math.Max(Math.Min(intRating, strValues.Length) - 1, 0)];
                }

                string strSuffix = string.Empty;
                char chrSuffix = strAvailExpr[strAvailExpr.Length - 1];
                if (chrSuffix == 'R')
                {
                    strSuffix = LanguageManager.GetString("String_AvailRestricted", GlobalOptions.Language);
                    // Remove the trailing character if it is "F" or "R".
                    strAvailExpr = strAvailExpr.Substring(0, strAvailExpr.Length - 1);
                }
                else if (chrSuffix == 'F')
                {
                    strSuffix = LanguageManager.GetString("String_AvailForbidden", GlobalOptions.Language);
                    // Remove the trailing character if it is "F" or "R".
                    strAvailExpr = strAvailExpr.Substring(0, strAvailExpr.Length - 1);
                }

                string strPrefix = string.Empty;
                char chrPrefix = strAvailExpr[0];
                if (chrPrefix == '+' || chrPrefix == '-')
                {
                    strPrefix = chrPrefix.ToString();
                    strAvailExpr = strAvailExpr.Substring(1, strAvailExpr.Length - 1);
                }

                strAvailExpr = strAvailExpr.CheapReplace("MinRating", () => nudRating.Minimum.ToString(GlobalOptions.InvariantCultureInfo));
                strAvailExpr = strAvailExpr.CheapReplace("Rating", () => nudRating.Value.ToString(GlobalOptions.InvariantCultureInfo));

                try
                {
                    int intAvail = Convert.ToInt32(CommonFunctions.EvaluateInvariantXPath(strAvailExpr)) + _intAvailModifier;
                    // Avail cannot go below 0.
                    if (intAvail < 0)
                        intAvail = 0;
                    lblAvail.Text = strPrefix + intAvail.ToString() + strSuffix;
                }
                catch (XPathException)
                {
                    lblAvail.Text = strAvail;
                }
            }
            else
            {
                lblAvail.Text = string.Empty;
            }

            // Cost.
            decimal decItemCost = 0;
            if (chkFree.Checked)
            {
                lblCost.Text = (0.0m).ToString(_objCharacter.Options.NuyenFormat, GlobalOptions.CultureInfo) + '¥';
            }
            else
            {
                string strCost = objXmlCyberware["cost"]?.InnerText;
                if (!string.IsNullOrEmpty(strCost))
                {
                    if (strCost.StartsWith("FixedValues("))
                    {
                        string[] strValues = strCost.TrimStart("FixedValues(", true).TrimEnd(')').Split(',');
                        strCost = strValues[Math.Max(Math.Min(intRating, strValues.Length) - 1, 0)];
                    }
                    // Check for a Variable Cost.
                    if (strCost.StartsWith("Variable("))
                    {
                        decimal decMin = 0;
                        decimal decMax = decimal.MaxValue;
                        strCost = strCost.TrimStart("Variable(", true).TrimEnd(')');
                        if (strCost.Contains('-'))
                        {
                            string[] strValues = strCost.Split('-');
                            decMin = Convert.ToDecimal(strValues[0], GlobalOptions.InvariantCultureInfo);
                            decMax = Convert.ToDecimal(strValues[1], GlobalOptions.InvariantCultureInfo);
                        }
                        else
                            decMin = Convert.ToDecimal(strCost.FastEscape('+'), GlobalOptions.InvariantCultureInfo);

                        lblCost.Text = decMax == decimal.MaxValue ?
                            decMin.ToString(_objCharacter.Options.NuyenFormat, GlobalOptions.CultureInfo) + "¥+" :
                            decMin.ToString(_objCharacter.Options.NuyenFormat, GlobalOptions.CultureInfo) + " - " + decMax.ToString(_objCharacter.Options.NuyenFormat, GlobalOptions.CultureInfo) + '¥';

                        decItemCost = decMin;
                    }
                    else
                    {
                        if (strCost.Contains("Parent Cost") || strCost.Contains("Parent Gear Cost"))
                        {
                            if (CyberwareParent != null)
                            {
                                strCost = strCost.CheapReplace("Parent Cost", () => CyberwareParent.Cost);
                                strCost = strCost.CheapReplace("Parent Gear Cost", () => CyberwareParent.Gear.AsParallel().Sum(x => x.TotalCost).ToString(GlobalOptions.InvariantCultureInfo));
                            }
                            else
                            {
                                strCost = strCost.Replace("Parent Cost", "0");
                                strCost = strCost.Replace("Parent Gear Cost", "0");
                            }
                        }
                        strCost = strCost.CheapReplace("MinRating", () => nudRating.Minimum.ToString(GlobalOptions.InvariantCultureInfo));
                        strCost = strCost.CheapReplace("Rating", () => nudRating.Value.ToString(GlobalOptions.InvariantCultureInfo));
                        try
                        {
                            decItemCost = (Convert.ToDecimal(CommonFunctions.EvaluateInvariantXPath(strCost), GlobalOptions.InvariantCultureInfo) * _decCostMultiplier * decGenetechCostModifier);
                            decItemCost *= 1 + (nudMarkup.Value / 100.0m);

                            if (chkBlackMarketDiscount.Checked)
                            {
                                decItemCost *= 0.9m;
                            }

                            lblCost.Text = decItemCost.ToString(_objCharacter.Options.NuyenFormat, GlobalOptions.CultureInfo) + '¥';
                        }
                        catch (XPathException)
                        {
                            lblCost.Text = strCost + '¥';
                        }
                    }
                }
                else
                    lblCost.Text = (0.0m).ToString(_objCharacter.Options.NuyenFormat, GlobalOptions.CultureInfo) + '¥';
            }

            // Test required to find the item.
            lblTest.Text = _objCharacter.AvailTest(decItemCost, lblAvail.Text);

            // Essence.
            int intESSDecimals = _objCharacter.Options.EssenceDecimals;
            string strESSFormat = "#,0";
            if (intESSDecimals > 0)
            {
                StringBuilder objESSFormat = new StringBuilder(".");
                for (int i = 0; i < intESSDecimals; ++i)
                    objESSFormat.Append('0');
                strESSFormat += objESSFormat.ToString();
            }
            decimal decESS = 0;
            if (!chkPrototypeTranshuman.Checked)
            {
                // Place the Essence cost multiplier in a variable that can be safely modified.
                decimal decCharacterESSModifier = 1.0m;

                if (!blnForceNoESSModifier)
                {
                    decCharacterESSModifier = CharacterESSMultiplier;
                    // If Basic Bioware is selected, apply the Basic Bioware ESS Multiplier.
                    if (strSelectCategory == "Basic")
                        decCharacterESSModifier -= (1 - BasicBiowareESSMultiplier);

                    if (nudESSDiscount.Visible)
                    {
                        decimal decDiscountModifier = nudESSDiscount.Value / 100.0m;
                        decCharacterESSModifier *= (1.0m - decDiscountModifier);
                    }

                    decCharacterESSModifier -= (1 - _decESSMultiplier);

                    decCharacterESSModifier *= CharacterTotalESSMultiplier;
                }
                string strEss = objXmlCyberware["ess"].InnerText;
                if (strEss.StartsWith("FixedValues("))
                {
                    string[] strValues = strEss.TrimStart("FixedValues(", true).TrimEnd(')').Split(',');
                    strEss = strValues[Math.Max(Math.Min(intRating, strValues.Length) - 1, 0)];
                }
                decESS = decCharacterESSModifier * Convert.ToDecimal(CommonFunctions.EvaluateInvariantXPath(strEss.Replace("Rating", nudRating.Value.ToString(GlobalOptions.InvariantCultureInfo))), GlobalOptions.InvariantCultureInfo);
                if (!_objCharacter.Options.DontRoundEssenceInternally)
                    decESS = decimal.Round(decESS, _objCharacter.Options.EssenceDecimals, MidpointRounding.AwayFromZero);
            }
            lblEssence.Text = decESS.ToString(strESSFormat, GlobalOptions.CultureInfo);
            if (objXmlCyberware["addtoparentess"] != null)
                lblEssence.Text = '+' + lblEssence.Text;

            // Capacity.
            // XPathExpression cannot evaluate while there are square brackets, so remove them if necessary.
            bool blnSquareBrackets = objXmlCyberware["capacity"]?.InnerText.Contains('[') ?? false;
            string strCapacity = objXmlCyberware["capacity"]?.InnerText ?? string.Empty;
            if (string.IsNullOrEmpty(strCapacity))
            {
                lblCapacity.Text = "0";
            }
            else
            {
                if (strCapacity.StartsWith("FixedValues("))
                {
                    string[] strValues = strCapacity.TrimStart("FixedValues(", true).TrimEnd(')').Split(',');
                    strCapacity = strValues[Math.Max(Math.Min(intRating, strValues.Length) - 1, 0)];
                }
                if (strCapacity == "[*]")
                    lblCapacity.Text = "*";
                else
                {
                    if (strCapacity.Contains("/["))
                    {
                        int intPos = strCapacity.IndexOf("/[");
                        string strFirstHalf = strCapacity.Substring(0, intPos);
                        string strSecondHalf = strCapacity.Substring(intPos + 1, strCapacity.Length - intPos - 1);

                        blnSquareBrackets = strFirstHalf.Contains('[');
                        if (blnSquareBrackets && strFirstHalf.Length > 1)
                            strFirstHalf = strFirstHalf.Substring(1, strCapacity.Length - 2);
                        lblCapacity.Text = CommonFunctions.EvaluateInvariantXPath(strFirstHalf.Replace("Rating", nudRating.Value.ToString(GlobalOptions.InvariantCultureInfo))).ToString();
                        if (blnSquareBrackets)
                            lblCapacity.Text = $"[{lblCapacity.Text}]";

                        strSecondHalf = strSecondHalf.Trim('[', ']');
                        strSecondHalf = '[' + CommonFunctions.EvaluateInvariantXPath(strSecondHalf.Replace("Rating", nudRating.Value.ToString(GlobalOptions.InvariantCultureInfo))).ToString() + ']';

                        lblCapacity.Text += '/' + strSecondHalf;
                    }
                    else
                    {
                        if (blnSquareBrackets)
                            strCapacity = strCapacity.Substring(1, strCapacity.Length - 2);
                        lblCapacity.Text = CommonFunctions.EvaluateInvariantXPath(strCapacity.Replace("Rating", nudRating.Value.ToString(GlobalOptions.InvariantCultureInfo))).ToString();
                        if (blnSquareBrackets)
                            lblCapacity.Text = $"[{lblCapacity.Text}]";
                    }
                }
            }
        }

        private bool _blnSkipListRefresh = false;
        private IList<ListItem> RefreshList(string strCategory, bool blnDoUIUpdate = true, bool blnTerminateAfterFirst = false)
        {
            if ((_blnLoading || _blnSkipListRefresh) && blnDoUIUpdate)
                return null;
            if (string.IsNullOrEmpty(strCategory))
            {
                if (blnDoUIUpdate)
                {
                    lstCyberware.BeginUpdate();
                    lstCyberware.DataSource = new List<ListItem>();
                    lstCyberware.EndUpdate();
                }
                return null;
            }

            string strFilter = "(" + _objCharacter.Options.BookXPath() +')';
            string strCategoryFilter = "(";
            if (strCategory != "Show All" && (_objCharacter.Options.SearchInCategoryOnly || txtSearch.TextLength == 0))
                strCategoryFilter += "category = \"" + strCategory + '\"';
            else
            {
                StringBuilder objCategoryFilter = new StringBuilder();
                foreach (ListItem objItem in cboCategory.Items)
                {
                    string strItem = objItem.Value.ToString();
                    if (!string.IsNullOrEmpty(strItem))
                        objCategoryFilter.Append("category = \"" + strItem + "\" or ");
                }
                if (objCategoryFilter.Length > 0)
                {
                    strCategoryFilter += objCategoryFilter.ToString().TrimEnd(" or ");
                }
            }
            strFilter += " and " + strCategoryFilter + " or category = \"None\")";
            
            if (_objCharacter.DEPEnabled && ParentVehicle == null)
                strFilter += " and (name = \"Essence Hole\" or name = \"Essence Antihole\" or mountsto)";
            else if (_objParentNode != null)
                strFilter += " and (requireparent or contains(capacity, \"[\")) and not(mountsto)";
            else
                strFilter += " and not(requireparent)";
            string strCurrentGradeId = cboGrade.SelectedValue?.ToString();
            Grade objCurrentGrade = string.IsNullOrEmpty(strCurrentGradeId) ? null : _lstGrades.FirstOrDefault(x => x.SourceId.ToString("D") == strCurrentGradeId);
            if (objCurrentGrade != null)
            {
                strFilter += " and (not(forcegrade) or forcegrade = \"None\" or forcegrade = \"" + objCurrentGrade.Name + "\")";
                if (objCurrentGrade.SecondHand)
                    strFilter += " and not(nosecondhand)";
            }

            strFilter += CommonFunctions.GenerateSearchXPath(txtSearch.Text);

            return BuildCyberwareList(_objXmlDocument.SelectNodes(_strNodeXPath + '[' + strFilter + ']'), blnDoUIUpdate, blnTerminateAfterFirst);
        }

        private IList<ListItem> BuildCyberwareList(XmlNodeList objXmlCyberwareList, bool blnDoUIUpdate = true, bool blnTerminateAfterFirst = false)
        {
            if (_blnLoading && blnDoUIUpdate)
                return null;

            List<ListItem> lstCyberwares = new List<ListItem>();

            bool blnCyberwareDisabled = _objCharacter.Improvements.Any(x => x.ImproveType == Improvement.ImprovementType.DisableCyberware && x.Enabled);
            bool blnBiowareDisabled = _objCharacter.Improvements.Any(x => x.ImproveType == Improvement.ImprovementType.DisableBioware && x.Enabled);
            string strCurrentGradeId = cboGrade.SelectedValue?.ToString();
            Grade objCurrentGrade = string.IsNullOrEmpty(strCurrentGradeId) ? null : _lstGrades.FirstOrDefault(x => x.SourceId.ToString("D") == strCurrentGradeId);
            foreach (XmlNode xmlCyberware in objXmlCyberwareList)
            {
                if (objCurrentGrade != null && xmlCyberware["forcegrade"] == null)
                {
                    if (_objCharacter.Improvements.Any(x => ((_objMode == Mode.Bioware && x.ImproveType == Improvement.ImprovementType.DisableBiowareGrade) || (_objMode != Mode.Bioware && x.ImproveType == Improvement.ImprovementType.DisableCyberwareGrade)) && objCurrentGrade.Name.Contains(x.ImprovedName) && x.Enabled))
                        continue;
                }
                if (blnCyberwareDisabled && xmlCyberware.SelectSingleNode("subsystems/cyberware") != null)
                {
                    continue;
                }
                if (blnBiowareDisabled && xmlCyberware.SelectSingleNode("subsystems/bioware") != null)
                {
                    continue;
                }
                XmlNode xmlTestNode = xmlCyberware.SelectSingleNode("forbidden/parentdetails");
                if (xmlTestNode != null)
                {
                    // Assumes topmost parent is an AND node
                    if (_objParentNode.ProcessFilterOperationNode(xmlTestNode, false))
                    {
                        continue;
                    }
                }
                xmlTestNode = xmlCyberware.SelectSingleNode("required/parentdetails");
                if (xmlTestNode != null)
                {
                    // Assumes topmost parent is an AND node
                    if (!_objParentNode.ProcessFilterOperationNode(xmlTestNode, false))
                    {
                        continue;
                    }
                }
                // TODO: Fix if someone has an amount of limbs different from the default amount
                if (!string.IsNullOrEmpty(_strHasModularMounts) && xmlCyberware["blocksmounts"] != null)
                {
                    IList<Cyberware> lstWareListToCheck = CyberwareParent == null ? (ParentVehicle == null ? _objCharacter.Cyberware : null) : CyberwareParent.Children;
                    if (xmlCyberware["selectside"] == null || !string.IsNullOrEmpty(CyberwareParent?.Location) || (lstWareListToCheck != null && lstWareListToCheck.Any(x => x.Location == "Left") && lstWareListToCheck.Any(x => x.Location == "Right")))
                    {
                        string[] astrBlockedMounts = xmlCyberware["blocksmounts"].InnerText.Split(',');
                        foreach (string strLoop in _strHasModularMounts.Split(','))
                        {
                            if (astrBlockedMounts.Contains(strLoop))
                            {
                                goto NextCyberware;
                            }
                        }
                    }
                }
                // TODO: Fix if someone has an amount of limbs different from the default amount
                if (!string.IsNullOrEmpty(_strDisallowedMounts) && xmlCyberware["modularmount"] != null)
                {
                    string strLoopMount = xmlCyberware["modularmount"].InnerText;
                    foreach (string strLoop in _strHasModularMounts.Split(','))
                    {
                        if (strLoopMount == strLoop)
                        {
                            goto NextCyberware;
                        }
                    }
                }
                string strMaxRating = xmlCyberware["rating"]?.InnerText;
                int intMaxRating = 0;
                string strMinRating = xmlCyberware["minrating"]?.InnerText;
                int intMinRating = 1;
                // If our rating tag is a complex property, check to make sure our maximum rating is not less than our minimum rating
                if ((!string.IsNullOrEmpty(strMaxRating) && !int.TryParse(strMaxRating, out intMaxRating)) || (!string.IsNullOrEmpty(strMinRating) && !int.TryParse(strMinRating, out intMinRating)))
                {
                    strMinRating = strMinRating.CheapReplace("MaximumSTR", () => (ParentVehicle != null ? Math.Max(1, ParentVehicle.TotalBody * 2) : _objCharacter.STR.TotalMaximum).ToString());
                    strMinRating = strMinRating.CheapReplace("MaximumAGI", () => (ParentVehicle != null ? Math.Max(1, ParentVehicle.Pilot * 2) : _objCharacter.AGI.TotalMaximum).ToString());
                    strMinRating = strMinRating.CheapReplace("MinimumSTR", () => (ParentVehicle != null ? ParentVehicle.TotalBody : 3).ToString());
                    strMinRating = strMinRating.CheapReplace("MinimumAGI", () => (ParentVehicle != null ? ParentVehicle.Pilot : 3).ToString());
                    try
                    {
                        intMinRating = Convert.ToInt32(CommonFunctions.EvaluateInvariantXPath(strMinRating));
                    }
                    catch (XPathException)
                    {
                        intMinRating = 1;
                    }

                    strMaxRating = strMaxRating.CheapReplace("MaximumSTR", () => (ParentVehicle != null ? Math.Max(1, ParentVehicle.TotalBody * 2) : _objCharacter.STR.TotalMaximum).ToString());
                    strMaxRating = strMaxRating.CheapReplace("MaximumAGI", () => (ParentVehicle != null ? Math.Max(1, ParentVehicle.Pilot * 2) : _objCharacter.AGI.TotalMaximum).ToString());
                    strMaxRating = strMaxRating.CheapReplace("MinimumSTR", () => (ParentVehicle != null ? ParentVehicle.TotalBody : 3).ToString());
                    strMaxRating = strMaxRating.CheapReplace("MinimumAGI", () => (ParentVehicle != null ? ParentVehicle.Pilot : 3).ToString());
                    try
                    {
                        intMaxRating = Convert.ToInt32(CommonFunctions.EvaluateInvariantXPath(strMaxRating));
                    }
                    catch (XPathException)
                    {
                    }
                    if (intMaxRating < intMinRating)
                        continue;
                }
                // Ex-Cons cannot have forbidden or restricted 'ware
                if (_objCharacter.ExCon && ParentVehicle == null && xmlCyberware["mountsto"] == null)
                {
                    Cyberware objParent = CyberwareParent;
                    bool blnAnyParentIsModular = !string.IsNullOrEmpty(objParent?.PlugsIntoModularMount);
                    while (objParent != null && !blnAnyParentIsModular)
                    {
                        objParent = CyberwareParent;
                        blnAnyParentIsModular = !string.IsNullOrEmpty(objParent?.PlugsIntoModularMount);
                    }

                    if (!blnAnyParentIsModular)
                    {
                        string strAvailExpr = xmlCyberware["avail"]?.InnerText ?? string.Empty;
                        if (strAvailExpr.StartsWith("FixedValues("))
                        {
                            string[] strValues = strAvailExpr.TrimStart("FixedValues(", true).TrimEnd(')').Split(',');
                            strAvailExpr = strValues[Math.Max(Math.Min(intMinRating - 1, strValues.Length - 1), 0)];
                        }
                        if (strAvailExpr.EndsWith('F', 'R'))
                        {
                            continue;
                        }
                    }
                }
                if (chkHideOverAvailLimit.Checked && !SelectionShared.CheckAvailRestriction(xmlCyberware, _objCharacter, intMinRating, xmlCyberware["forcegrade"]?.InnerText == "None" ? 0 : _intAvailModifier))
                    continue;
                if (ParentVehicle == null && !xmlCyberware.RequirementsMet(_objCharacter))
                    continue;
                
                lstCyberwares.Add(new ListItem(xmlCyberware["id"]?.InnerText, xmlCyberware["translate"]?.InnerText ?? xmlCyberware["name"]?.InnerText));
                if (blnTerminateAfterFirst)
                    break;
                NextCyberware:;
            }
            if (blnDoUIUpdate)
            {
                lstCyberwares.Sort(CompareListItems.CompareNames);

                string strOldSelected = lstCyberware.SelectedValue?.ToString();
                _blnLoading = true;
                lstCyberware.BeginUpdate();
                lstCyberware.ValueMember = "Value";
                lstCyberware.DisplayMember = "Name";
                lstCyberware.DataSource = lstCyberwares;
                _blnLoading = false;
                if (!string.IsNullOrEmpty(strOldSelected))
                    lstCyberware.SelectedValue = strOldSelected;
                else
                    lstCyberware.SelectedIndex = -1;

                lstCyberware.EndUpdate();
            }

            return lstCyberwares;
        }

        /// <summary>
        /// Lock the Grade so it cannot be changed.
        /// </summary>
        public void LockGrade()
        {
            cboGrade.Enabled = false;
            _blnLockGrade = true;
        }

        /// <summary>
        /// Accept the selected item and close the form.
        /// </summary>
        private void AcceptForm()
        {
            string strSelectedId = lstCyberware.SelectedValue?.ToString();
            if (string.IsNullOrEmpty(strSelectedId))
                return;
            if (cboGrade.Text.StartsWith("*"))
            {
                MessageBox.Show(
                    LanguageManager.GetString("Message_BannedGrade", GlobalOptions.Language),
                    LanguageManager.GetString("MessageTitle_BannedGrade", GlobalOptions.Language),
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            XmlNode objCyberwareNode = _objXmlDocument.SelectSingleNode(_strNodeXPath + "[id = \"" + strSelectedId + "\"]");
            if (objCyberwareNode == null)
                return;

            if (_objCharacter.Options.EnforceCapacity && _objParentNode != null && objCyberwareNode["capacity"].InnerText.Contains('['))
            {
                // Capacity.
                // XPathExpression cannot evaluate while there are square brackets, so remove them if necessary.
                string strCapacity = objCyberwareNode["capacity"].InnerText;
                strCapacity = strCapacity.Substring(1, strCapacity.Length - 2);
                if (strCapacity.StartsWith("FixedValues("))
                {
                    string[] strValues = strCapacity.TrimStart("FixedValues(", true).TrimEnd(')').Split(',');
                    strCapacity = strValues[Math.Max(Math.Min(decimal.ToInt32(nudRating.Value), strValues.Length) - 1, 0)];
                }
                decimal decCapacity = 0;

                if (strCapacity != "*")
                {
                    decCapacity = Convert.ToDecimal(CommonFunctions.EvaluateInvariantXPath(strCapacity.Replace("Rating", nudRating.Value.ToString(GlobalOptions.InvariantCultureInfo))), GlobalOptions.InvariantCultureInfo);
                }
                if (MaximumCapacity - decCapacity < 0)
                {
                    MessageBox.Show(
                        LanguageManager.GetString("Message_OverCapacityLimit", GlobalOptions.Language)
                            .Replace("{0}", MaximumCapacity.ToString("#,0.##", GlobalOptions.CultureInfo))
                            .Replace("{1}", decCapacity.ToString("#,0.##", GlobalOptions.CultureInfo)),
                        LanguageManager.GetString("MessageTitle_OverCapacityLimit", GlobalOptions.Language),
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
            }
            if (ParentVehicle == null && !objCyberwareNode.RequirementsMet(_objCharacter, LanguageManager.GetString(_objMode == Mode.Cyberware ? "String_SelectPACKSKit_Cyberware" : "String_SelectPACKSKit_Bioware", GlobalOptions.Language)))
                return;

            string strForceGrade = objCyberwareNode["forcegrade"]?.InnerText;
            if (!string.IsNullOrEmpty(strForceGrade))
            {
                SelectedGrade = _lstGrades.FirstOrDefault(x => x.Name == strForceGrade);
            }
            else
            {
                strForceGrade = cboGrade.SelectedValue?.ToString();
                if (!string.IsNullOrEmpty(strForceGrade))
                    SelectedGrade = _lstGrades.FirstOrDefault(x => x.SourceId.ToString("D") == cboGrade.SelectedValue?.ToString());
                else
                    return;
            }
            _sStrSelectCategory = (_objCharacter.Options.SearchInCategoryOnly || txtSearch.TextLength == 0) ? _strSelectedCategory : objCyberwareNode["category"]?.InnerText;
            _sStrSelectGrade = SelectedGrade.SourceId.ToString("D");
            SelectedCyberware = strSelectedId;
            SelectedRating = decimal.ToInt32(nudRating.Value);
            BlackMarketDiscount = chkBlackMarketDiscount.Checked;

            if (nudESSDiscount.Visible)
                SelectedESSDiscount = decimal.ToInt32(nudESSDiscount.Value);

            DialogResult = DialogResult.OK;
        }

        private bool _blnPopulatingGrades = false;
        /// <summary>
        /// Populate the list of Cyberware Grades.
        /// </summary>
        /// <param name="blnIgnoreSecondHand">Whether or not Secon-Hand Grades should be added to the list.</param>
        private void PopulateGrades(bool blnIgnoreSecondHand = false, bool blnForce = false, string strForceGrade = "", bool blnHideBannedGrades = true)
        {
            if (_blnPopulatingGrades)
                return;
            _blnPopulatingGrades = true;
            if (blnForce || blnIgnoreSecondHand != _blnIgnoreSecondHand || _strForceGrade != strForceGrade || cboGrade.Items.Count == 0)
            {
                _blnIgnoreSecondHand = blnIgnoreSecondHand;
                _strForceGrade = strForceGrade;
                List<ListItem> lstGrade = new List<ListItem>(5);
                foreach (Grade objWareGrade in _lstGrades)
                {
                    if (objWareGrade.SourceId.ToString("D") == _strNoneGradeId && (string.IsNullOrEmpty(strForceGrade) || strForceGrade != _strNoneGradeId))
                        continue;
                    if (_objCharacter.Improvements.Any(x => (WindowMode == Mode.Bioware && x.ImproveType == Improvement.ImprovementType.DisableBiowareGrade || WindowMode != Mode.Bioware && x.ImproveType == Improvement.ImprovementType.DisableCyberwareGrade)
                                                                   && objWareGrade.Name.Contains(x.ImprovedName) && x.Enabled))
                        continue;
                    if (blnIgnoreSecondHand && objWareGrade.SecondHand)
                        continue;
                    if (_objCharacter.AdapsinEnabled && _objMode == Mode.Cyberware)
                    {
                        if (!objWareGrade.Adapsin && _lstGrades.Any(x => objWareGrade.Name.Contains(x.Name)))
                        {
                            continue;
                        }
                    }
                    else if (objWareGrade.Adapsin)
                        continue;
                    if (_objCharacter.BurnoutEnabled)
                    {
                        if (!objWareGrade.Burnout && _lstGrades.Any(x => objWareGrade.Burnout && objWareGrade.Name.Contains(x.Name)))
                        {
                            continue;
                        }
                    }
                    else if (objWareGrade.Burnout)
                        continue;
                    if (blnHideBannedGrades && !_objCharacter.Created && _objCharacter.BannedWareGrades.Any(s => objWareGrade.Name.Contains(s)))
                        continue;
                    if (!blnHideBannedGrades && !_objCharacter.Created &&
                        _objCharacter.BannedWareGrades.Any(s => objWareGrade.Name.Contains(s)))
                    {
                        lstGrade.Add(new ListItem(objWareGrade.SourceId.ToString("D"), $"*{objWareGrade.DisplayName(GlobalOptions.Language)}"));
                    }
                    else
                    {
                        lstGrade.Add(new ListItem(objWareGrade.SourceId.ToString("D"), objWareGrade.DisplayName(GlobalOptions.Language)));
                    }
                }
                
                string strOldSelected = cboGrade.SelectedValue?.ToString();
                bool blnOldSkipListRefresh = _blnSkipListRefresh;
                if (strForceGrade == _strNoneGradeId || strOldSelected == _strNoneGradeId || lstGrade.Any(x => x.Value.ToString() == strOldSelected))
                    _blnSkipListRefresh = true;
                bool blnOldLoading = _blnLoading;
                _blnLoading = true;
                cboGrade.BeginUpdate();
                cboGrade.ValueMember = "Value";
                cboGrade.DisplayMember = "Name";
                cboGrade.DataSource = lstGrade;
                _blnLoading = blnOldLoading;
                if (!string.IsNullOrEmpty(strForceGrade))
                    cboGrade.SelectedValue = strForceGrade;
                else if (cboGrade.SelectedIndex <= 0 && !string.IsNullOrWhiteSpace(strOldSelected))
                    cboGrade.SelectedValue = strOldSelected;
                if (cboGrade.SelectedIndex == -1 && lstGrade.Count > 0)
                    cboGrade.SelectedIndex = 0;

                cboGrade.EndUpdate();

                _blnSkipListRefresh = blnOldSkipListRefresh;
            }
            _blnPopulatingGrades = false;
        }

        private bool _blnPopulatingCategories = false;
        private void PopulateCategories()
        {
            if (_blnPopulatingCategories)
                return;
            _blnPopulatingCategories = true;
            XmlNodeList objXmlCategoryList;
            if (_strSubsystems.Length > 0)
            {
                // Populate the Cyberware Category list.
                string strSubsystem = ". = \"";
                if (_strSubsystems.Contains(','))
                {
                    strSubsystem += _strSubsystems.Replace(",", "\" or . = \"");
                }
                else
                {
                    strSubsystem += _strSubsystems;
                }
                objXmlCategoryList = _objXmlDocument.SelectNodes("/chummer/categories/category[" + strSubsystem + "\"]");
            }
            else
            {
                objXmlCategoryList = _objXmlDocument.SelectNodes("/chummer/categories/category");
            }
            List<ListItem> lstCategory = new List<ListItem>();
            foreach (XmlNode objXmlCategory in objXmlCategoryList)
            {
                // Make sure the category contains items that we can actually display
                if (RefreshList(objXmlCategory.InnerText, false, true).Count > 0)
                {
                    string strInnerText = objXmlCategory.InnerText;
                    lstCategory.Add(new ListItem(strInnerText, objXmlCategory.Attributes?["translate"]?.InnerText ?? strInnerText));
                }
            }

            lstCategory.Sort(CompareListItems.CompareNames);

            if (lstCategory.Count > 0)
            {
                lstCategory.Insert(0, new ListItem("Show All", LanguageManager.GetString("String_ShowAll", GlobalOptions.Language)));
            }

            string strOldSelected = _strSelectedCategory;
            string strOldSelectedCyberware = lstCyberware.SelectedValue?.ToString();
            bool blnOldLoading = _blnLoading;
            _blnLoading = true;
            cboCategory.BeginUpdate();
            cboCategory.ValueMember = "Value";
            cboCategory.DisplayMember = "Name";
            cboCategory.DataSource = lstCategory;
            _blnLoading = blnOldLoading;
            cboCategory.SelectedValue = strOldSelected;
            if (cboCategory.SelectedIndex == -1 && lstCategory.Count > 0)
                cboCategory.SelectedIndex = 0;

            cboCategory.EndUpdate();

            if (!string.IsNullOrEmpty(strOldSelectedCyberware))
                lstCyberware.SelectedValue = strOldSelectedCyberware;

            _blnPopulatingCategories = false;
        }

        private void MoveControls()
        {
            int intWidth = Math.Max(lblRatingLabel.Width, lblEssenceLabel.Width);
            intWidth = Math.Max(intWidth, lblCapacityLabel.Width);
            intWidth = Math.Max(intWidth, lblAvailLabel.Width);
            intWidth = Math.Max(intWidth, lblCostLabel.Width);

            nudRating.Left = lblRatingLabel.Left + intWidth + 6;
            lblEssence.Left = lblEssenceLabel.Left + intWidth + 6;
            lblCapacity.Left = lblCapacityLabel.Left + intWidth + 6;
            lblAvail.Left = lblAvailLabel.Left + intWidth + 6;
            lblCost.Left = lblCostLabel.Left + intWidth + 6;

            lblSource.Left = lblSourceLabel.Left + lblSourceLabel.Width + 6;
            lblTest.Left = lblTestLabel.Left + lblTestLabel.Width + 6;
            nudESSDiscount.Left = lblESSDiscountLabel.Left + lblESSDiscountLabel.Width + 6;
            lblESSDiscountPercentLabel.Left = nudESSDiscount.Left + nudESSDiscount.Width;

            lblSearchLabel.Left = txtSearch.Left - 6 - lblSearchLabel.Width;
        }
        #endregion
    }
}
