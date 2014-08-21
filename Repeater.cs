using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Web.Mvc;
using System.Web.Routing;
using System.Web.UI;
using Countersoft.Foundation.Commons.Extensions;
using Countersoft.Gemini;
using Countersoft.Gemini.Extensibility;
using Countersoft.Gemini.Commons;
using Countersoft.Gemini.Extensibility.Apps;
using Countersoft.Gemini.Infrastructure;
using Countersoft.Gemini.Infrastructure.Apps;
using System.Linq;
using Countersoft.Gemini.Commons.Dto;
using Countersoft.Gemini.Commons.Meta;
using Countersoft.Gemini.Commons.System;
using Countersoft.Gemini.Commons.Entity;
using Countersoft.Foundation.Commons.Core;
using Countersoft.Gemini.Infrastructure.Helpers;
using Countersoft.Gemini.Models;
using Countersoft.Gemini.Commons.Permissions;

namespace ItemRepeater
{
    [AppType(AppTypeEnum.FullPage),
    AppGuid("CEF9D547-5106-49D6-8B08-F753AE1E23BF"),
    AppControlGuid("6F81C360-0B61-49CF-BFC7-AAF6B21F7C34"),
    AppAuthor("Countersoft"),
    AppKey("Repeater"),
    AppName("Repeater"),
    AppControlUrl("view"),
    AppDescription("Repeats Items into the future"),
    AppRequiresViewPermission(true)]
    public class Repeater : BaseAppController
    {
        public static string RepeatSessionView = "RepeatSessionView";

        public override WidgetResult Show(IssueDto issue = null)
        {
            WidgetResult result = new WidgetResult();

            RepeaterModel model = GetRepeatingModel();
         
            result.Markup = new WidgetMarkup("views\\Repeater.cshtml", model);

            result.Success = true;

            return result;
        }

        public override WidgetResult Caption(IssueDto issue = null)
        {
            WidgetResult result = new WidgetResult();

            result.Success = true;

            result.Markup.Html = AppName;

            return result;
        }

        public RepeaterModel GetRepeatingModel()
        {
            RepeaterModel model = new RepeaterModel();

            var filter = new IssuesFilter();

            if (IsSessionFilter() || !CurrentCard.Options.ContainsKey(AppGuid))
                filter = HttpSessionManager.GetFilter(CurrentCard.Id, IssuesFilter.CreateProjectFilter(CurrentUser.Entity.Id, CurrentProject.Entity.Id));
            else if (CurrentCard.Options.ContainsKey(AppGuid))
                filter = CurrentCard.Options[AppGuid].FromJson<IssuesFilter>();
            else
                filter = CurrentCard.Filter;

            if (filter.Repeat.IsEmpty() || filter.Repeat == "-|3")
            {
                filter.Repeat = "-|1";
            }

            var transformedFilter = ItemFilterManager.TransformFilter(filter);
            SetCurrentProjectFromFilter(transformedFilter);

            model.Filter = IssueFilterHelper.PopulateModel(model.Filter, filter, transformedFilter, PermissionsManager, ItemFilterManager, IssueFilterHelper.GetViewableFields(filter, ProjectManager, MetaManager), false);
            model.GeminiDateFormat = CurrentUser.GeminiDateFormat;
            model.BaseUrl = string.Format("{0}workspace/{1}/", UserContext.Url, CurrentCard.Id);

            var allIssues = IssueManager.GetFiltered(filter);

            var allIssuesRelatedRepeating = allIssues.Count > 0 ? allIssues.FindAll(s => s.Repeated.HasValue() || s.Entity.OriginatorData.HasValue() && s.Entity.OriginatorType == IssueOriginatorType.Repeat) : new List<IssueDto>();

            var masterIssues = allIssuesRelatedRepeating.Count > 0 ? allIssuesRelatedRepeating.FindAll(i => i.Repeated.HasValue()) : new List<IssueDto>();

            List<string> repeatValues = new List<string>();
            repeatValues.Add("-|1");
            repeatValues.Add("-|2");
            repeatValues.Add("-|3");
            
            int totalNumberOfRepeatedItems = 0;

            if (masterIssues.Count > 0)
            {
                foreach (var masterIssue in masterIssues)
                {
                    ItemsGrid item = new ItemsGrid();

                    item.MasterItem = masterIssue;
                    item.RepeatedItems = allIssuesRelatedRepeating.FindAll(s => s.OriginatorData.HasValue() && s.OriginatorData.Equals(masterIssue.Entity.Id.ToString()));

                    if (item.RepeatedItems.Count > 0 || filter.Repeat == "-|2" || !repeatValues.Contains(filter.Repeat))
                    {
                        item.RepeatedItems = item.RepeatedItems.OrderBy("Created").ToList();
                        IssueDto lastRepeated = IssueManager.GetLastCreatedIssueForOriginator(IssueOriginatorType.Repeat, masterIssue.Id.ToString());

                        if (lastRepeated != null) item.LastRepitition = lastRepeated.Created.ToString(UserContext.User.DateFormat);
                    }                   
                    
                    
                    //Create Next repitition
                    RepeatParser repeat = new RepeatParser(masterIssue.Repeated);

                    for (DateTime date = DateTime.Today; item.NextRepitition.IsEmpty() ; date = date.AddDays(1))
                    {
                        repeat.CurrentDateTime = date;

                        DateTime lastRepeatedDate = masterIssue.Created;

                        if (item.RepeatedItems.Count > 0) lastRepeatedDate = item.RepeatedItems.Last().Entity.Created;

                        if (item.RepeatedItems.Count >= repeat.MaximumRepeats)
                        {
                            break;
                        }

                        if (repeat.NeedsToRepeat(lastRepeatedDate))
                        {
                            item.NextRepitition = date.ToShortDateString();
                            break;
                        }
                    }
                    
                    totalNumberOfRepeatedItems += item.RepeatedItems.Count;

                    model.Items.Add(item);
                }
            }
            else if (allIssuesRelatedRepeating.Count > 0)
            {
                foreach (var repeatedIssue in allIssuesRelatedRepeating)
                {
                    ItemsGrid item = new ItemsGrid() { MasterItem = repeatedIssue, RepeatedItems = new List<IssueDto>() };
                    model.Items.Add(item);
                }
            }

            model.ItemCount = masterIssues.Count + totalNumberOfRepeatedItems;
            return model;
        }

        [AppUrl("create")]
        public ActionResult Create(string startDate, string endDate, int itemId)
        {
            DateTime? startDateTime = ParseDateString.GetDateForString(startDate);
            DateTime? endDateTime = ParseDateString.GetDateForString(endDate);

            if (startDateTime == null || endDateTime == null || endDateTime < startDateTime) return JsonError("Make sure Start Date and End Date are valid dates");

            //If selection range is bigger than 3 years set the last date to max 3 years
            if (((endDateTime.Value - DateTime.Today).TotalDays / 365) > 3)
            {
                endDateTime = DateTime.Today.AddYears(3);

                if (startDateTime > endDateTime) startDateTime = endDateTime;
            }

            var closedStatuses = MetaManager.StatusGetClosed();

            List<IssueLinkType> linkTypes = IssueManager.GeminiContext.Meta.LinkTypeGet();
            IssueLinkType repeatedLinkType = linkTypes.Find(t => string.Compare(t.Label, "Repeated", true) == 0);

            if (repeatedLinkType == null && linkTypes.Count > 0) repeatedLinkType = linkTypes[0];

            var issue = IssueManager.Get(itemId);

            RepeatParser repeat = new RepeatParser(issue.Repeated);

            List<IssueDto> repeatedIssues = IssueManager.GetItemsForOriginator(IssueOriginatorType.Repeat, issue.Id.ToString());

            if (repeatedIssues.Count > 0)
            {
                var previousItemsToDelete = repeatedIssues.FindAll(c => c.Created.Date() >= startDateTime.Value && c.Created.Date() <= endDateTime.Value && !closedStatuses.Contains(c.Entity.StatusId));

                foreach (var item in previousItemsToDelete)
                {
                    IssueManager.Delete(item.Entity.Id);
                }
            }



            for (DateTime date = startDateTime.Value; date <= endDateTime.Value; date = date.AddDays(1))
            {
                repeat.CurrentDateTime = date.Date();

                IssueDto lastRepeated = IssueManager.GetLastCreatedIssueForOriginator(IssueOriginatorType.Repeat, issue.Id.ToString());

                DateTime lastRepeatedDate = issue.Created;

                if (lastRepeated != null && lastRepeated.Entity.IsExisting) lastRepeatedDate = lastRepeated.Created;

                if (repeat.MaximumRepeats > 0)
                {
                    repeatedIssues = IssueManager.GetItemsForOriginator(IssueOriginatorType.Repeat, issue.Id.ToString());

                    if (repeatedIssues != null && repeatedIssues.Count >= repeat.MaximumRepeats) continue;
                }

                //If last item was created into the future do this
                if (lastRepeatedDate > date.Date())
                {
                    List<IssueDto> tmpRepeatedIssues = IssueManager.GetItemsForOriginator(IssueOriginatorType.Repeat, issue.Id.ToString());

                    List<IssueDto> ItemsBeforeStartDate = tmpRepeatedIssues.FindAll(i => i.Created < date.Date());

                    if (ItemsBeforeStartDate.Count == 0)
                    {
                        lastRepeatedDate = issue.Created;
                    }
                    else
                    {
                        lastRepeatedDate = ItemsBeforeStartDate.OrderBy("Created").Last().Created;
                    }

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

                    if (issue.Entity.StartDate.HasValue && issue.Entity.DueDate.HasValue
                        && issue.Entity.StartDate != new DateTime() && issue.Entity.DueDate != new DateTime())
                    {
                        TimeSpan tsDates = issue.Entity.DueDate.Value - issue.Entity.StartDate.Value;

                        issue.Entity.DueDate = date.AddDays(tsDates.TotalDays);

                        issue.Entity.StartDate = date.Date();
                    }
                    else
                    {
                        issue.Entity.StartDate = null;

                        issue.Entity.DueDate = null;
                    }

                    int issueId = issue.Id;

                    issue.Entity.Created = date;

                    IssueDto repeated = IssueManager.Create(issue.Entity);

                    if (repeated.Entity.Id > 0)
                    {
                        string statment = "update gemini_issues set created = @created where issueid = @issueid";

                        SQLService.Instance.ExecuteQuery(statment, new { created = new DateTime(date.Year, date.Month, date.Day, 8, 0, 0).ToUtc(UserContext.User.TimeZone), issueid = repeated.Entity.Id });
                    }

                    if (customFields != null && customFields.Count > 0)
                    {
                        foreach (var field in customFields)
                        {
                            try
                            {
                                field.Entity.Id = 0;

                                field.Entity.IssueId = repeated.Entity.Id;

                                field.Entity.ProjectId = repeated.Entity.ProjectId;

                                CustomFieldManager.Update(new CustomFieldData(field.Entity));
                            }
                            catch (Exception ex)
                            {
                                LogException(ex);
                            }
                        }
                    }

                    if (repeatedLinkType != null)
                    {
                        IssueManager.IssueLinkCreate(repeated.Entity.Id, issueId, repeatedLinkType.Id);
                    }
                }
            }

            return JsonSuccess();
        }

        [AppUrl("getitemgrid")]
        public ActionResult GetItemGrid()
        {
            RepeaterModel model = GetRepeatingModel();

            return JsonSuccess(new { Html = RenderPartialViewToString(this, AppManager.Instance.GetAppUrl(AppGuid, "views/_Grid.cshtml"), model) });
        }

        [AppUrl("popup")]
        public ActionResult GetCreatePopup(int itemId)
        {
            GeneratePopupModel model = new GeneratePopupModel();

            model.GeminiDateFormat = CurrentUser.GeminiDateFormat;
            model.Item = IssueManager.Get(itemId);

            return Json(new JsonResponse()
            {
                Success = true,
                Message = "",
                Result = new { Html = RenderPartialViewToString(this, AppManager.Instance.GetAppUrl(AppGuid, "views/_GeneratePopup.cshtml"), model) }
            });


        }

    }
}
