using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.TeamFoundation.Client;
using Microsoft.TeamFoundation.VersionControl.Client;
using Microsoft.TeamFoundation.WorkItemTracking.Client;
using NDesk.Options;

namespace TFS
{
    class Program
    {
        private static void Main(string[] args)
        {
            var tfsUrl = "";
            var pbiNumber = 0;
            var optionSet = new OptionSet
            {
                {"pbi=", "The {PBI} Number", (int pbi) => pbiNumber = pbi},
                {"tfs=", "The {TFSURL} for the TFS Collection to use", url => { tfsUrl = url; }}
            };
            optionSet.Parse(args);

            var tpc = new TfsTeamProjectCollection(new Uri(tfsUrl));
            var workItemStore = tpc.GetService<WorkItemStore>();
            var vcServer = tpc.GetService<VersionControlServer>();

            var changesets = GetChangesets(workItemStore, vcServer, pbiNumber);

            Output("{0} changesets", changesets.Count);
            foreach (var changeset in changesets.OrderBy(n => n.Key).Select(n => n.Value))
            {
                Output("{0} - {1}", changeset.ChangesetId, changeset.Comment);
            }

            Output("");

            var files =
                changesets.Values.SelectMany(changeset => changeset.Changes,
                    (changeset, change) => new {change, changeset})
                    .Select(a => new
                    {
                        ChangeType = a.change.ChangeType.HasFlag(ChangeType.Add) ? ChangeType.Add : a.change.ChangeType,
                        FileName = a.change.Item.ServerItem.Substring(@"$/PwC-CMS/Development/Source/PwC CMS/".Length),
                        Changeset = a.changeset
                    })
                    .GroupBy(a => new {a.ChangeType, a.FileName}, a => a.Changeset)
                    .OrderBy(g => g.Key.ChangeType).ThenBy(g => g.Key.FileName)
                    .ToList();

            Output("{0} files", files.Count);
            foreach (var file in files)
            {
                Output("{0} - {1}", file.Key.ChangeType, file.Key.FileName);
                foreach (var changeset in file)
                {
                    Output("\t{0} - {1}", changeset.ChangesetId, changeset.Comment);
                }
            }
        }

        private static void Output(string value, params object[] parms)
        {
            Debug.WriteLine(value, parms);
            Console.WriteLine(value, parms);
        }

        private static Dictionary<int, Changeset> GetChangesets(WorkItemStore workItemStore,
            VersionControlServer vcServer, int pbiNumber)
        {
            var changesets = new Dictionary<int, Changeset>();

            var pbi = GetWorkItem(workItemStore, "Product Backlog Item", pbiNumber);
            BuildChangesets(vcServer, pbi.Links.OfType<ExternalLink>(), changesets);
            foreach (var pbiRelatedLink in pbi.Links.OfType<RelatedLink>())
            {
                var task = GetWorkItem(workItemStore, "Task", pbiRelatedLink.RelatedWorkItemId);
                if (task != null)
                    BuildChangesets(vcServer, task.Links.OfType<ExternalLink>(), changesets);
            }
            return changesets;
        }

        private static void BuildChangesets(VersionControlServer vcServer, IEnumerable<ExternalLink> taskExternalLinks, Dictionary<int, Changeset> changesets)
        {
            foreach (var taskExternalLink in taskExternalLinks)
            {
                var fileName = Path.GetFileName(taskExternalLink.LinkedArtifactUri);
                if (fileName == null) continue;
                var changesetNumber = int.Parse(fileName);

                var changeset = vcServer.GetChangeset(changesetNumber);

                if (!changesets.ContainsKey(changesetNumber))
                    changesets.Add(changesetNumber, changeset);
            }
        }

        private static WorkItem GetWorkItem(WorkItemStore workItemStore, string workItemType, int workItemId)
        {
            var queryResults = workItemStore.Query(string.Format(@"
                Select [State], [Title] 
                From WorkItems
                Where [Work Item Type] = '{0}' And Id = {1}
                Order By [State] Asc, [Changed Date] Desc", workItemType, workItemId));
            return queryResults.Count == 0 ? null : queryResults[0];
        }
    }
}
