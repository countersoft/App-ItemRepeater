using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Countersoft.Foundation.Commons.Extensions;
using Countersoft.Gemini.Commons;
using Countersoft.Gemini.Commons.Dto;
using Countersoft.Gemini.Commons.Entity;
using Countersoft.Gemini.Commons.Meta;
using Countersoft.Gemini.Commons.System;
using Countersoft.Gemini.Contracts.Business;
using Countersoft.Gemini.Infrastructure.Managers;
using Countersoft.Gemini.Infrastructure.TimerJobs;
using Countersoft.Gemini.Extensibility.Apps;

namespace ItemRepeater
{
    [AppType(AppTypeEnum.Timer),
    AppGuid("CEF9D547-5106-49D6-8B08-F753AE1E23BF"),
    AppName("Item Repeater"),
    AppDescription("Repeats items at specified intervals")]
    public class RepeatEngine : TimerJob
    {
        public override bool Run(IssueManager issueManager)
        {
/*#if DEBUG
            Debugger.Launch();
#endif*/
            LogDebugMessage("Repeat Processing");

            try
            {
                List<IssueDto> issues = issueManager.GetRepeated();

                if (issues == null || issues.Count == 0) return true;

                List<IssueLinkType> linkTypes = issueManager.GeminiContext.Meta.LinkTypeGet();

                IssueLinkType repeatedLinkType = linkTypes.Find(t => string.Compare(t.Label, "Repeated", true) == 0);

                if (repeatedLinkType == null && linkTypes.Count > 0) repeatedLinkType = linkTypes[0];

                foreach (IssueDto issue in issues)
                {
                    RepeatParser repeat = new RepeatParser(issue.Repeated);

                    repeat.CurrentDateTime = DateTime.UtcNow.ToLocal(issueManager.UserContext.User.TimeZone);
                                        
                    IssueDto lastRepeated = issueManager.GetLastCreatedIssueForOriginator(IssueOriginatorType.Repeat, issue.Id.ToString());

                    DateTime lastRepeatedDate = issue.Created;

                    if (lastRepeated != null && lastRepeated.Entity.IsExisting) lastRepeatedDate = lastRepeated.Created;

                    if (repeat.MaximumRepeats > 0)
                    {
                        List<IssueDto> repeatedIssues = issueManager.GetItemsForOriginator(IssueOriginatorType.Repeat, issue.Id.ToString());

                        if (repeatedIssues != null && repeatedIssues.Count >= repeat.MaximumRepeats) continue;
                    }

                    if (repeat.NeedsToRepeat(lastRepeatedDate))
                    {
                        var customFields = issue.CustomFields;
                        
                        issue.Attachments = new List<IssueAttachmentDto>();

                        issue.Entity.Repeated = string.Empty;

                        issue.Entity.OriginatorData = issue.Entity.Id.ToString();

                        issue.Entity.OriginatorType = IssueOriginatorType.Repeat;

                        issue.Entity.ParentIssueId = null;
                        issue.Entity.IsParent = false;

                        issue.Entity.StatusId = 0;

                        issue.Entity.ResolutionId = 0;
                        issue.Entity.Watchers = string.Empty;
                        issue.Entity.LoggedHours = 0;
                        issue.Entity.LoggedMinutes = 0;

                        if (issue.Entity.StartDate.HasValue && issue.Entity.DueDate.HasValue
                            && issue.Entity.StartDate != new DateTime() && issue.Entity.DueDate != new DateTime())
                        {
                            TimeSpan tsDates = issue.Entity.DueDate.Value - issue.Entity.StartDate.Value;

                            issue.Entity.DueDate = DateTime.Today.AddDays(tsDates.TotalDays);

                            issue.Entity.StartDate = DateTime.Today;
                        }
                        else
                        {
                            issue.Entity.StartDate = null;

                            issue.Entity.DueDate = null;
                        }

                        int issueId = issue.Id;

                        IssueDto repeated = issueManager.Create(issue.Entity);

                        if (customFields != null && customFields.Count > 0)
                        {
                            var customFieldManager = new CustomFieldManager(issueManager);

                            foreach (var field in customFields)
                            {
                                try
                                {
                                    field.Entity.Id = 0;

                                    field.Entity.IssueId = repeated.Entity.Id;

                                    field.Entity.ProjectId = repeated.Entity.ProjectId;

                                    customFieldManager.Update(new CustomFieldData(field.Entity));
                                }
                                catch (Exception ex)
                                {
                                    LogException(ex);
                                }
                            }
                        }

                        if (repeatedLinkType != null)
                        {
                            issueManager.IssueLinkCreate(repeated.Entity.Id, issueId, repeatedLinkType.Id);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogException(ex);
            }

            return true;
        }

        public override void Shutdown()
        {
            
        }

        public override TimerJobSchedule GetInterval(IGlobalConfigurationWidgetStore dataStore)
        {
            var data = dataStore.Get<TimerJobSchedule>(AppGuid);

            if (data == null || data.Value == null || (data.Value.Cron.IsEmpty() && data.Value.IntervalInHours.GetValueOrDefault() == 0 && data.Value.IntervalInMinutes.GetValueOrDefault() == 0))
            {
                return new TimerJobSchedule(60);
            }

            return data.Value;
        }

        public override void SetInterval(IGlobalConfigurationWidgetStore dataStore, TimerJobSchedule schedule)
        {
            dataStore.Save<TimerJobSchedule>(AppGuid, schedule);   
        }
    }
}
