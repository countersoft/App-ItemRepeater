using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Countersoft.Gemini.Commons.Dto;
using Countersoft.Gemini.Commons.Entity;
using Countersoft.Gemini.Models;

namespace ItemRepeater
{
    public class RepeaterModel
    {
        public RepeaterModel()
        {
            Filter = new InstantItemFilterModel();
            Items = new List<ItemsGrid>();           
        }

        public InstantItemFilterModel Filter { get; set; }

        public string GeminiDateFormat { get; set; }
        public string BaseUrl { get; set; }

        public List<ItemsGrid> Items { get; set; }

        public int ItemCount { get; set; }
    }

    public class ItemsGrid
    {
        public IssueDto MasterItem { get; set; }       
        public List<IssueDto> RepeatedItems { get; set; }
        public string LastRepitition { get; set; }
        public string NextRepitition { get; set; }
    }

    public class GeneratePopupModel
    {
        public string GeminiDateFormat { get; set; }
        public IssueDto Item { get; set; }
    }

    public class PageSettings : IssuesGridFilter
    {
        public PageData PageData { get; set; }

        public PageSettings()
        {
            PageData = new PageData();
        }
    }

    public class PageData
    {
        public int versionId { get; set; }
        public int projectId { get; set; }
    }    
}
