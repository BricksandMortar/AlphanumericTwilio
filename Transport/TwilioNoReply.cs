using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Linq;
using System.Net.Mail;
using System.Text;

using Rock.Attribute;
using Rock.Data;
using Rock.Model;
using Rock.Web.Cache;

using Twilio;

namespace Rock.Communication.Transport
{
    /// <summary>
    /// Communication transport for sending no-reply SMS messages using Twilio
    /// </summary>
    [Description( "Sends a no-reply communication through Twilio API" )]
    [Export( typeof( TransportComponent ) )]
    [ExportMetadata( "ComponentName", "No-Reply Twilio" )]
    [TextField( "SID", "Your Twilio Account SID (find at https://www.twilio.com/user/account)", true, "", "", 0 )]
    [TextField( "Token", "Your Twilio Account Token", true, "", "", 1 )]

    public class TwilioNoReply : TransportComponent
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
                    communication.Status == Model.CommunicationStatus.Approved &&
                    communication.Recipients.Where( r => r.Status == Model.CommunicationRecipientStatus.Pending ).Any() &&
                    (!communication.FutureSendDateTime.HasValue || communication.FutureSendDateTime.Value.CompareTo( RockDateTime.Now ) <= 0) )
                {
                    // Remove all non alpha numeric from fromValue
                    string fromValue = new string( communication.GetMediumDataValue( "NoReply_FromValue" ).Where( c => char.IsLetterOrDigit( c ) || char.IsWhiteSpace( c ) ).ToArray() );

                    if ( !string.IsNullOrWhiteSpace( fromValue ) )
                    {
                        string accountSid = GetAttributeValue( "SID" );
                        string authToken = GetAttributeValue( "Token" );
                        var twilio = new TwilioRestClient( accountSid, authToken );

                        var historyService = new HistoryService( rockContext );
                        var recipientService = new CommunicationRecipientService( rockContext );

                        var personEntityTypeId = EntityTypeCache.Read( "Rock.Model.Person" ).Id;
                        var communicationEntityTypeId = EntityTypeCache.Read( "Rock.Model.Communication" ).Id;
                        var communicationCategoryId = CategoryCache.Read( Rock.SystemGuid.Category.HISTORY_PERSON_COMMUNICATIONS.AsGuid(), rockContext ).Id;

                        var globalConfigValues = GlobalAttributesCache.GetMergeFields( null );

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
                                        var mergeObjects = recipient.CommunicationMergeValues( globalConfigValues );
                                        StringBuilder messageBuilder = new StringBuilder( communication.GetMediumDataValue( "NoReply_Message" ) );
                                        if ( !string.IsNullOrWhiteSpace( communication.GetMediumDataValue( "NoReply_SenderPhone" ) ) )
                                        {
                                            messageBuilder.Append( string.Format( "\nThis message was sent by {0} on behalf of {1} from a no reply number. To reply to this message send your response to {2}.", communication.GetMediumDataValue( "NoReply_SenderName" ), Rock.Web.Cache.GlobalAttributesCache.Read().GetValueFormatted( "OrganizationName" ), communication.GetMediumDataValue( "NoReply_SenderPhone" ) ) );
                                        }
                                        else
                                        {
                                            messageBuilder.Append( string.Format( "\nThis message was sent by {0} on behalf of {1} from a no reply number. To reply to this message contact {0} directly.", communication.GetMediumDataValue( "NoReply_SenderName" ), Rock.Web.Cache.GlobalAttributesCache.Read().GetValueFormatted( "OrganizationName" ) ) );
                                        }
                                        string message = messageBuilder.ToString();
                                        message = message.ResolveMergeFields( mergeObjects );

                                        string twilioNumber = phoneNumber.Number;
                                        if ( !string.IsNullOrWhiteSpace( phoneNumber.CountryCode ) )
                                        {
                                            twilioNumber = "+" + phoneNumber.CountryCode + phoneNumber.Number;
                                        }

                                        var globalAttributes = Rock.Web.Cache.GlobalAttributesCache.Read();
                                        string callbackUrl = globalAttributes.GetValue( "PublicApplicationRoot" ) + "Webhooks/Twilio.ashx";

                                        var response = twilio.SendMessage( fromValue, twilioNumber, message, callbackUrl );

                                        recipient.Status = CommunicationRecipientStatus.Delivered;
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
                                                Summary = "Sent No Reply SMS message.",
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
                                        recipient.StatusNote = "No Phone Number with Messaging Enabled";
                                    }
                                }
                                catch ( Exception ex )
                                {
                                    recipient.Status = CommunicationRecipientStatus.Failed;
                                    recipient.StatusNote = "Twilio Exception: " + ex.Message;
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

                    string message = string.Empty;
                    string senderPhone = string.Empty;
                    string senderName = string.Empty;
                    mediumData.TryGetValue( "NoReply_Message", out message );
                    mediumData.TryGetValue( "NoReply_SenderPhone", out senderPhone );
                    mediumData.TryGetValue( "NoReply_SenderName", out senderName );
                    StringBuilder messageBuilder = new StringBuilder( message );
                    if ( !string.IsNullOrWhiteSpace( senderPhone ) )
                    {
                        messageBuilder.Append( string.Format( "\nThis message was sent by {0} on behalf of {1} from a no reply number. To reply to this message send your response to {2}.", senderName, Rock.Web.Cache.GlobalAttributesCache.Read().GetValueFormatted( "OrganizationName" ), senderPhone ) );
                    }
                    else
                    {
                        messageBuilder.Append( string.Format( "\nThis message was sent by {0} on behalf of {1} from a no reply number. To reply to this message contact {0} directly.", senderName, Rock.Web.Cache.GlobalAttributesCache.Read().GetValueFormatted( "OrganizationName" ) ) );
                    }
                    message = messageBuilder.ToString();

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
            try
            {
                var globalAttributes = GlobalAttributesCache.Read();

                string fromValue = from;
                if ( !string.IsNullOrWhiteSpace( fromValue ) )
                {
                    string accountSid = GetAttributeValue( "SID" );
                    string authToken = GetAttributeValue( "Token" );
                    var twilio = new TwilioRestClient( accountSid, authToken );

                    StringBuilder messageBuilder = new StringBuilder( body );
                    messageBuilder.Append( string.Format( "\nThis message was sent by {0} from a no reply number.", Rock.Web.Cache.GlobalAttributesCache.Read().GetValueFormatted( "OrganizationName" ) ) );
                    string message = messageBuilder.ToString();
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
        /// <param name="attachments">Attachments.</param>
        /// <exception cref="System.NotImplementedException"></exception>
        public override void Send( List<string> recipients, string from, string subject, string body, string appRoot = null, string themeRoot = null, List<Attachment> attachments = null )
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Sends the specified recipients.
        /// </summary>
        /// <param name="recipients">The recipients.</param>
        /// <param name="from">From.</param>
        /// <param name="fromName">From name.</param>
        /// <param name="subject">The subject.</param>
        /// <param name="body">The body.</param>
        /// <param name="appRoot">The application root.</param>
        /// <param name="themeRoot">The theme root.</param>
        /// <param name="attachments">The attachments.</param>
        /// <exception cref="System.NotImplementedException"></exception>
        public override void Send( List<string> recipients, string from, string fromName, string subject, string body, string appRoot = null, string themeRoot = null, List<Attachment> attachments = null )
        {
            throw new NotImplementedException();
        }
    }
}
