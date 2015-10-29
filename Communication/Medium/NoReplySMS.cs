using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text;

using Rock;
using Rock.Data;
using Rock.Model;
using Rock.Communication;
using Rock.Web.UI.Controls.Communication;

namespace com.bricksandmortar.Communication.Medium
{
    /// <summary>
    /// An no-reply SMS communication
    /// </summary>
    [Description("An no reply SMS communication")]
    [Export(typeof(MediumComponent))]
    [ExportMetadata("ComponentName", "No Reply SMS")]
    public class NoReplySMS : MediumComponent
    {
        /// <summary>
        /// Gets the control.
        /// </summary>
        /// <param name="useSimpleMode">if set to <c>true</c> [use simple mode].</param>
        /// <returns></returns>
        public override MediumControl GetControl(bool useSimpleMode)
        {
            return new Rock.Web.UI.Controls.Communication.NoReplySMS(useSimpleMode);
        }

        /// <summary>
        /// Gets the HTML preview.
        /// </summary>
        /// <param name="communication">The communication.</param>
        /// <param name="person">The person.</param>
        /// <returns></returns>
        public override string GetHtmlPreview(Rock.Model.Communication communication, Person person)
        {
            var rockContext = new RockContext();

            // Requery the Communication object
            communication = new CommunicationService(rockContext).Get(communication.Id);

            var globalAttributes = Rock.Web.Cache.GlobalAttributesCache.Read();
            var mergeValues = Rock.Web.Cache.GlobalAttributesCache.GetMergeFields(null);

            if (person != null)
            {
                mergeValues.Add("Person", person);

                var recipient = communication.Recipients.Where(r => r.PersonAlias != null && r.PersonAlias.PersonId == person.Id).FirstOrDefault();
                if (recipient != null)
                {
                    // Add any additional merge fields created through a report
                    foreach (var mergeField in recipient.AdditionalMergeValues)
                    {
                        if (!mergeValues.ContainsKey(mergeField.Key))
                        {
                            mergeValues.Add(mergeField.Key, mergeField.Value);
                        }
                    }
                }
            }

            string message = communication.GetMediumDataValue("NoReply_Message");
            return message.ResolveMergeFields(mergeValues);
        }

        /// <summary>
        /// Gets the read-only message details.
        /// </summary>
        /// <param name="communication">The communication.</param>
        /// <returns></returns>
        public override string GetMessageDetails(Rock.Model.Communication communication)
        {
            StringBuilder sb = new StringBuilder();

            AppendMediumData(communication, sb, "NoReply_FromValue");
            AppendMediumData(communication, sb, "NoReply_Message");
            return sb.ToString();
        }

        private void AppendMediumData(Rock.Model.Communication communication, StringBuilder sb, string key)
        {
            string value = communication.GetMediumDataValue(key);
            if (!string.IsNullOrWhiteSpace(value))
            {
                AppendMediumData(sb, key, value);
            }
        }

        private void AppendMediumData(StringBuilder sb, string key, string value)
        {
            sb.AppendFormat("<div class='form-group'><label class='control-label'>{0}</label><p class='form-control-static'>{1}</p></div>",
                key.SplitCase(), value);
        }

        /// <summary>
        /// Sends the specified communication.
        /// </summary>
        /// <param name="communication">The communication.</param>
        public override void Send(Rock.Model.Communication communication)
        {
            var rockContext = new RockContext();
            var communicationService = new CommunicationService(rockContext);

            communication = communicationService.Get(communication.Id);

            if (communication != null &&
                communication.Status == Rock.Model.CommunicationStatus.Approved &&
                communication.Recipients.Where(r => r.Status == Rock.Model.CommunicationRecipientStatus.Pending).Any() &&
                (!communication.FutureSendDateTime.HasValue || communication.FutureSendDateTime.Value.CompareTo(RockDateTime.Now) <= 0))
            {
                // Update any recipients that should not get sent the communication
                var recipientService = new CommunicationRecipientService(rockContext);
                foreach (var recipient in recipientService.Queryable("PersonAlias.Person")
                    .Where(r =>
                       r.CommunicationId == communication.Id &&
                       r.Status == CommunicationRecipientStatus.Pending)
                    .ToList())
                {
                    var person = recipient.PersonAlias.Person;
                    if (person.IsDeceased ?? false)
                    {
                        recipient.Status = CommunicationRecipientStatus.Failed;
                        recipient.StatusNote = "Person is deceased!";
                    }
                }

                rockContext.SaveChanges();
            }

            base.Send(communication);
        }

        /// <summary>
        /// Gets a value indicating whether [supports bulk communication].
        /// </summary>
        /// <value>
        /// <c>true</c> if [supports bulk communication]; otherwise, <c>false</c>.
        /// </value>
        public override bool SupportsBulkCommunication
        {
            get
            {
                return false;
            }
        }

    }
}