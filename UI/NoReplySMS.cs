using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.UI;
using System.Web.UI.WebControls;

using Rock.Model;

namespace Rock.Web.UI.Controls.Communication
{
    /// <summary>
    /// SMS Communication Medium control
    /// </summary>
    public class NoReplySMS : MediumControl
    {
        #region UI Controls

        private RockTextBox tbFrom;
        private RockLiteral lFrom;
        private RockCheckBox cbAppendUserInfo;
        private RockControlWrapper rcwMessage;
        private MergeFieldPicker mfpMessage;
        private RockTextBox tbMessage;
        private HiddenField hfSenderPhone;
        private HiddenField hfSenderName;

        #endregion

        #region Properties
        /// <summary>
        /// Gets or sets a value indicating whether [use simple mode].
        /// </summary>
        /// <value>
        ///   <c>true</c> if [use simple mode]; otherwise, <c>false</c>.
        /// </value>
        public bool UseSimpleMode
        {
            get { return ViewState["UseSimpleMode"] as Boolean? ?? false; }
            set { ViewState["UseSimpleMode"] = value; }
        }

        /// <summary>
        /// Gets or sets the medium data.
        /// </summary>
        /// <value>
        /// The medium data.
        /// </value>
        public override Dictionary<string, string> MediumData
        {
            get
            {
                EnsureChildControls();
                var data = new Dictionary<string, string>();
                if ( !UseSimpleMode )
                {
                    data.Add( "NoReply_FromValue", tbFrom.Text );
                    data.Add( "NoReply_AppendUserInfo", cbAppendUserInfo.Checked.ToString() );
                }
                else
                {
                    data.Add( "NoReply_FromValue", lFrom.Text );
                    data.Add( "NoReply_AppendUserInfo", "True" );
                }
                data.Add( "NoReply_Message", tbMessage.Text );
                data.Add( "NoReply_SenderName", hfSenderName.Value );
                data.Add( "NoReply_SenderPhone", hfSenderPhone.Value );
                return data;
            }

            set
            {
                EnsureChildControls();
                lFrom.Text = GetDataValue( value, "NoReply_FromValue" );
                tbFrom.Text = GetDataValue( value, "NoReply_FromValue" );
                tbMessage.Text = GetDataValue( value, "NoReply_Message" );
                cbAppendUserInfo.Checked = GetDataValue( value, "NoReply_AppendUserInfo" ).AsBoolean();
                hfSenderName.Value = GetDataValue( value, "NoReply_SenderName" );
                hfSenderPhone.Value = GetDataValue( value, "NoReply_SenderPhone" );
            }
        }

        #endregion

        #region Constructor

        /// <summary>
        /// Initializes a new instance of the <see cref="Email"/> class.
        /// </summary>
        public NoReplySMS( )
            : base()
        {

        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Email"/> class.
        /// </summary>
        /// <param name="useSimpleMode">if set to <c>true</c> [use simple mode].</param>
        public NoReplySMS( bool useSimpleMode )
            : this()
        {
            UseSimpleMode = useSimpleMode;
        }
        #endregion

        #region CompositeControl Methods

        /// <summary>
        /// Called by the ASP.NET page framework to notify server controls that use composition-based implementation to create any child controls they contain in preparation for posting back or rendering.
        /// </summary>
        protected override void CreateChildControls( )
        {
            base.CreateChildControls();
            Controls.Clear();

            tbFrom = new RockTextBox();
            tbFrom.ID = string.Format( "tbFrom_{0}", this.ID );
            tbFrom.MaxLength = 11;
            tbFrom.Label = "From";
            tbFrom.Help = "The name the recipient will see as the message originating from.";
            Controls.Add( tbFrom );

            lFrom = new RockLiteral();
            lFrom.ID = string.Format( "lFrom_{0}", this.ID );
            lFrom.Label = "From";
            Controls.Add( lFrom );

            rcwMessage = new RockControlWrapper();
            rcwMessage.ID = string.Format( "rcwMessage_{0}", this.ID );
            rcwMessage.Label = "Message";
            rcwMessage.Help = "<span class='tip tip-lava'></span>";
            Controls.Add( rcwMessage );

            mfpMessage = new MergeFieldPicker();
            mfpMessage.ID = string.Format( "mfpMergeFields_{0}", this.ID );
            mfpMessage.MergeFields.Clear();
            mfpMessage.MergeFields.Add( "GlobalAttribute" );
            mfpMessage.MergeFields.Add( "Rock.Model.Person" );
            mfpMessage.CssClass += " pull-right margin-b-sm";
            mfpMessage.SelectItem += mfpMergeFields_SelectItem;
            rcwMessage.Controls.Add( mfpMessage );

            tbMessage = new RockTextBox();
            tbMessage.ID = string.Format( "tbTextMessage_{0}", this.ID );
            tbMessage.TextMode = TextBoxMode.MultiLine;
            tbMessage.Rows = 3;
            rcwMessage.Controls.Add( tbMessage );

            cbAppendUserInfo = new RockCheckBox();
            cbAppendUserInfo.ID = string.Format( "cbAppendUserInfo_{0}", this.ID );
            cbAppendUserInfo.Label = "Add contact information to message?";
            cbAppendUserInfo.Help = "Append your message with name and phone number to allow receipients to contact you?";
            cbAppendUserInfo.Checked = true;
            Controls.Add( cbAppendUserInfo );

            hfSenderName = new HiddenField();
            hfSenderName.ID = string.Format( "hfSenderName_{0}", this.ID );
            Controls.Add( hfSenderName );

            hfSenderPhone = new HiddenField();
            hfSenderPhone.ID = string.Format( "hfSenderPhone_{0}", this.ID );
            Controls.Add( hfSenderPhone );
        }

        /// <summary>
        /// Gets or sets the validation group.
        /// </summary>
        /// <value>
        /// The validation group.
        /// </value>
        public override string ValidationGroup
        {
            get
            {
                EnsureChildControls();
                return tbMessage.ValidationGroup;
            }
            set
            {
                EnsureChildControls();
                tbFrom.ValidationGroup = value;
                mfpMessage.ValidationGroup = value;
                tbMessage.ValidationGroup = value;
            }
        }

        /// <summary>
        /// On new communicaiton, initializes controls from sender values
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <exception cref="System.NotImplementedException"></exception>
        public override void InitializeFromSender( Person sender )
        {
            EnsureChildControls();
            try
            {
                hfSenderPhone.Value = sender.PhoneNumbers
                .Where( p => p.IsMessagingEnabled == true )
                .FirstOrDefault()
                .NumberFormattedWithCountryCode;
            }
            catch ( Exception )
            {
            }

            hfSenderName.Value = sender.FullName;

            string organizationName = Rock.Web.Cache.GlobalAttributesCache.Read().GetValueFormatted( "OrganizationName" );
            if ( organizationName.Length > 11 )
            {
                string organizationAbbreviation = Rock.Web.Cache.GlobalAttributesCache.Read().GetValueFormatted( "OrganizationAbbreviation" );
                if ( !string.IsNullOrWhiteSpace( organizationAbbreviation ) & organizationAbbreviation.Length < 11 )
                {
                    organizationName = organizationAbbreviation;
                }
                else
                {
                    organizationName = organizationName.Replace( " ", string.Empty );
                    organizationName = organizationName.Substring( 0, 11 );
                }

            }

            if ( string.IsNullOrWhiteSpace( tbFrom.Text ) & !IsTemplate )
            {
                tbFrom.Text = organizationName;
            }
            if ( string.IsNullOrWhiteSpace( lFrom.Text ) )
            {
                lFrom.Text = organizationName;
            }
        }

        /// <summary>
        /// Outputs server control content to a provided <see cref="T:System.Web.UI.HtmlTextWriter" /> object and stores tracing information about the control if tracing is enabled.
        /// </summary>
        /// <param name="writer">The <see cref="T:System.Web.UI.HtmlTextWriter" /> object that receives the control content.</param>
        public override void RenderControl( HtmlTextWriter writer )
        {
            tbFrom.Required = !IsTemplate;
            tbMessage.Required = !IsTemplate;

            if ( !UseSimpleMode )
            {

                tbFrom.RenderControl( writer );
                cbAppendUserInfo.RenderControl( writer );
            }
            else
            {
                lFrom.RenderControl( writer );
            }
            rcwMessage.RenderControl( writer );
            hfSenderName.RenderControl( writer );
            hfSenderPhone.RenderControl( writer );
        }

        #endregion

        #region Events

        void mfpMergeFields_SelectItem( object sender, EventArgs e )
        {
            EnsureChildControls();
            tbMessage.Text += mfpMessage.SelectedMergeField;
            mfpMessage.SetValue( string.Empty );
        }

        #endregion
    }

}
