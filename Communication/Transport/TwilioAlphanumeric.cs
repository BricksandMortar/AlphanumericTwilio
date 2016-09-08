using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text;

using Rock.Communication;
using Rock.Attribute;
using Rock.Data;
using Rock.Model;
using Rock.Web.Cache;

using Twilio;
using Rock;

namespace com.bricksandmortarstudio.Communication.Transport
{
    /// <summary>
    /// Communication transport for sending no-reply SMS messages using Twilio
    /// </summary>
    [Description( "Sends a no reply communication through the Twilio API" )]
    [Export( typeof( TransportComponent ) )]
    [ExportMetadata( "ComponentName", "Twilio Alphanumeric SMS" )]
    [TextField( "SID", "Your Twilio Account SID (find at https://www.twilio.com/user/account)", true, "", "", 0 )]
    [TextField( "Token", "Your Twilio Account Token", true, "", "", 1 )]
    [TextField( "Optional Footer", "<span class='tip tip-lava'>{{Sender}}</span> This footer will automatically be added to all simple mode communications. Use this to provide a way for your recipients to opt-out or as additional context for who is sending the messages.", true, "This message was sent from a no reply number. Contact us on {{ GlobalAttribute.OrganizationPhone }} to opt out of future messages.", key: "footer" )]

    public class TwilioAlphanumeric : TransportComponent
    {
        /// <summary>
        /// Sends the specified communication.
        /// </summary>
        /// <param name="communication">The communication.</param>
        /// <exception cref="System.NotImplementedException"></exception>
        public override void Send( Rock.Model.Communication communication )
        {
            using ( var rockContext = new RockContext() )
            {
                // Requery the Communication
                communication = new CommunicationService( rockContext ).Get( communication.Id );

                if ( communication != null &&
                    communication.Status == CommunicationStatus.Approved &&
                    communication.Recipients.Where( r => r.Status == CommunicationRecipientStatus.Pending ).Any() &&
                    ( !communication.FutureSendDateTime.HasValue || communication.FutureSendDateTime.Value.CompareTo( RockDateTime.Now ) <= 0 ) )
                {
                    // Remove all non alpha numeric from fromValue
                    string fromValue = communication.GetMediumDataValue( "NoReply_FromValue" ).ToCharArray().Where( c => char.IsLetterOrDigit( c ) || char.IsWhiteSpace( c ) ).ToString();
                    string senderGuid = communication.GetMediumDataValue( "SenderGuid" );
                    if ( !string.IsNullOrWhiteSpace( fromValue ) && !string.IsNullOrWhiteSpace( senderGuid ) )
                    {
                        string accountSid = GetAttributeValue( "SID" );
                        string authToken = GetAttributeValue( "Token" );
                        var twilio = new TwilioRestClient( accountSid, authToken );
                        var historyService = new HistoryService( rockContext );
                        var recipientService = new CommunicationRecipientService( rockContext );

                        var personEntityTypeId = EntityTypeCache.Read( "Rock.Model.Person" ).Id;
                        var communicationEntityTypeId = EntityTypeCache.Read( "Rock.Model.Communication" ).Id;
                        var communicationCategoryId = CategoryCache.Read( Rock.SystemGuid.Category.HISTORY_PERSON_COMMUNICATIONS.AsGuid(), rockContext ).Id;
                        var sender = new PersonService( rockContext ).Get( senderGuid.AsGuid() );
                        var mergeFields = Rock.Web.Cache.GlobalAttributesCache.GetMergeFields( null );
                        if ( sender != null )
                        {
                            mergeFields.Add( "Sender", sender );
                        }

                        bool recipientFound = true;
                        while ( recipientFound )
                        {
                            var recipient = Rock.Model.Communication.GetNextPending( communication.Id, rockContext );
                            if ( recipient != null )
                            {
                                try
                                {
                                    var phoneNumber = recipient.PersonAlias.Person.PhoneNumbers
                                        .Where( p => p.IsMessagingEnabled )
                                        .FirstOrDefault();

                                    if ( phoneNumber != null )
                                    {
                                        // Create merge field dictionary
                                        var mergeObjects = recipient.CommunicationMergeValues( mergeFields );
                                        string message = communication.GetMediumDataValue( "NoReply_Message" );
                                        string footer = GetAttributeValue( "footer" );
                                        if ( communication.GetMediumDataValue( "NoReply_AppendUserInfo" ).AsBoolean( false ) && !string.IsNullOrEmpty( footer ) )
                                        {
                                            message += "\n " + footer;
                                        }
                                        else
                                        {
                                            message += "\n This message was sent on behalf of {{ GlobalAttribute.OrganizationName }} from a no reply number.";
                                        }

                                        message = message.ReplaceWordChars();
                                        message = message.ResolveMergeFields( mergeObjects );

                                        string twilioNumber = phoneNumber.Number;
                                        if ( !string.IsNullOrWhiteSpace( phoneNumber.CountryCode ) )
                                        {
                                            twilioNumber = "+" + phoneNumber.CountryCode + phoneNumber.Number;
                                        }

                                        var globalAttributes = Rock.Web.Cache.GlobalAttributesCache.Read();
                                        string callbackUrl = globalAttributes.GetValue( "PublicApplicationRoot" ) + "Webhooks/Twilio.ashx";

                                        var response = twilio.SendMessage( fromValue, twilioNumber, message, callbackUrl );

                                        if ( response.Status.ToLower() == "failed" )
                                        {
                                            recipient.Status = CommunicationRecipientStatus.Failed;
                                            break;
                                        }
                                        else
                                        {
                                            recipient.Status = CommunicationRecipientStatus.Delivered;
                                        }

                                        recipient.TransportEntityTypeName = this.GetType().FullName;
                                        recipient.UniqueMessageId = response.Sid;

                                        try
                                        {
                                            historyService.Add( new History
                                            {
                                                CreatedByPersonAliasId = communication.SenderPersonAliasId,
                                                EntityTypeId = personEntityTypeId,
                                                CategoryId = communicationCategoryId,
                                                EntityId = recipient.PersonAlias.PersonId,
                                                Summary = "Sent an alphanumeric SMS message from " + fromValue + ".",
                                                Caption = message.Truncate( 200 ),
                                                RelatedEntityTypeId = communicationEntityTypeId,
                                                RelatedEntityId = communication.Id
                                            } );
                                        }
                                        catch ( Exception ex )
                                        {
                                            ExceptionLogService.LogException( ex, null );
                                        }

                                    }
                                    else
                                    {
                                        recipient.Status = CommunicationRecipientStatus.Failed;
                                        recipient.StatusNote = "No phone number with messaging enabled";
                                    }
                                }
                                catch ( Exception ex )
                                {
                                    recipient.Status = CommunicationRecipientStatus.Failed;
                                    recipient.StatusNote = "Twilio Exception: " + ex.Message;
                                    ExceptionLogService.LogException( ex, null );
                                }
                                rockContext.SaveChanges();
                            }
                            else
                            {
                                recipientFound = false;
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Sends the specified template.
        /// </summary>
        /// <param name="template">The template.</param>
        /// <param name="recipients">The recipients.</param>
        /// <param name="appRoot">The application root.</param>
        /// <param name="themeRoot">The theme root.</param>
        /// <exception cref="System.NotImplementedException"></exception>
        public override void Send( SystemEmail template, List<RecipientData> recipients, string appRoot, string themeRoot )
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Sends the specified medium data to the specified list of recipients.
        /// </summary>
        /// <param name="mediumData">The medium data.</param>
        /// <param name="recipients">The recipients.</param>
        /// <param name="appRoot">The application root.</param>
        /// <param name="themeRoot">The theme root.</param>
        /// <exception cref="System.NotImplementedException"></exception>
        public override void Send( Dictionary<string, string> mediumData, List<string> recipients, string appRoot, string themeRoot )
        {
            try
            {
                var globalAttributes = GlobalAttributesCache.Read();
                string fromValue = string.Empty;
                mediumData.TryGetValue( "NoReply_FromValue", out fromValue );
                if ( !string.IsNullOrWhiteSpace( fromValue ) )
                {
                    string accountSid = GetAttributeValue( "SID" );
                    string authToken = GetAttributeValue( "Token" );
                    var twilio = new TwilioRestClient( accountSid, authToken );
                    var mergeFields = Rock.Web.Cache.GlobalAttributesCache.GetMergeFields( null );
                    string message = mediumData["NoReply_Message"];
                    string appendUserInfo = mediumData["NoReply_AppendUserInfo"];
                    string footer = GetAttributeValue( "footer" );
                    if ( appendUserInfo.AsBoolean( false ) && !string.IsNullOrEmpty( footer ) )
                    {
                        message += footer;
                    }
                    else
                    {
                        message += "\nThis message was sent on behalf of {{ GlobalAttribute.OrganizationName }} from a no reply number.";
                    }
                    message = message.ResolveMergeFields( mergeFields );

                    if ( !string.IsNullOrWhiteSpace( themeRoot ) )
                    {
                        message = message.Replace( "~~/", themeRoot );
                    }

                    if ( !string.IsNullOrWhiteSpace( appRoot ) )
                    {
                        message = message.Replace( "~/", appRoot );
                        message = message.Replace( @" src=""/", @" src=""" + appRoot );
                        message = message.Replace( @" href=""/", @" href=""" + appRoot );
                    }

                    foreach ( var recipient in recipients )
                    {
                        var response = twilio.SendMessage( fromValue, recipient, message );
                    }
                }
            }

            catch ( Exception ex )
            {
                ExceptionLogService.LogException( ex, null );
            }
        }

        /// <summary>
        /// Sends the specified recipients.
        /// </summary>
        /// <param name="recipients">The recipients.</param>
        /// <param name="from">From.</param>
        /// <param name="subject">The subject.</param>
        /// <param name="body">The body.</param>
        /// <param name="appRoot">The application root.</param>
        /// <param name="themeRoot">The theme root.</param>
        /// <exception cref="System.NotImplementedException"></exception>
        public override void Send( List<string> recipients, string from, string subject, string body, string appRoot = null, string themeRoot = null )
        {
            throw new NotImplementedException();
        }

        public override void Send( List<string> recipients, string from, string fromName, string subject, string body, string appRoot = null, string themeRoot = null, List<System.Net.Mail.Attachment> attachments = null )
        {
            throw new NotImplementedException();
        }

        public override void Send( List<string> recipients, string from, string subject, string body, string appRoot = null, string themeRoot = null, List<System.Net.Mail.Attachment> attachments = null )
        {
            throw new NotImplementedException();
        }
    }
}
