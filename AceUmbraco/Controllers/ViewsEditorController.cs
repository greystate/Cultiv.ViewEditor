﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Web;
using System.Web.Hosting;
using System.Web.Http;
using Newtonsoft.Json;
using Umbraco.Web.WebApi;
using File = System.IO.File;

namespace AceUmbraco.Controllers
{
    public class ViewsEditorController : UmbracoAuthorizedApiController
    {
        public ViewFile GetByPath(string path)
        {
            if (path == "-1" || path.EndsWith(".cshtml") == false)
            {
                var scaffold = "@inherits UmbracoTemplatePage\r{\r\t\r}\r";

                if (path.ToLowerInvariant().StartsWith("MacroPartials".ToLowerInvariant()))
                {
                    scaffold = "@inherits Umbraco.Web.Macros.PartialViewMacroPage\r";
                }

                return new ViewFile { Value = scaffold, FileName = path, Layout = null, Sections = null };
            }

            var contents = GetViewContents(path);

            var currentLayout = GetLayout(contents);
            var layouts = currentLayout;

            var sections = new List<Section>();

            while (layouts != null)
            {
                var layoutContents = GetViewContents(layouts);
                sections = GetSections(layoutContents).OrderBy(x => x.Name).ToList();
                layouts = GetLayout(layoutContents);
            }

            return new ViewFile { Value = contents, FileName = path, Layout = currentLayout, Sections = sections };
        }

        private static string GetViewContents(string path)
        {
            string contents = null;

            if (path.IsValidViewFile())
            {
                var file = new FileInfo(HttpContext.Current.Request.MapPath("~/Views/" + path));
                using (var reader = new StreamReader(file.FullName))
                {
                    contents = reader.ReadToEnd();
                }
            }

            return contents;
        }

        public HttpResponseMessage GetDeleteByPath(string path)
        {
            if (path.IsValidViewFile())
            {
                //Get the path to the file
                var filePath = HttpContext.Current.Request.MapPath("~/Views/" + path);

                //Check file exists on disk - & hasn't been removed on disk by user
                if (File.Exists(filePath))
                {
                    //Delete the file
                    File.Delete(filePath);

                    return Request.CreateResponse(HttpStatusCode.OK);
                }
            }

            //File does not exist
            return Request.CreateResponse(HttpStatusCode.NotFound);
        }


        public HttpResponseMessage PutSaveFolder([FromBody] ViewFolder folder)
        {
            if (folder.FolderName.IsValidFolder() == false)
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);

            Directory.CreateDirectory(folder.Parent == "-1"
                ? HttpContext.Current.Request.MapPath("~/Views/" + folder.FolderName)
                : HttpContext.Current.Request.MapPath("~/Views/" + folder.Parent + "/" + folder.FolderName));

            return new HttpResponseMessage(HttpStatusCode.OK);
        }

        public HttpResponseMessage PutSaveView([FromBody]ViewFile view)
        {
            var ct = JsonConvert.DeserializeObject<string>(view.Value);

            if (view.IsNew)
            {
                view.FileName = view.NewFileName;
            }

            var filenameChanged = view.FileName.ToLowerInvariant() != view.NewFileName.ToLowerInvariant();

            if (filenameChanged && File.Exists(HttpContext.Current.Request.MapPath("~/Views/" + view.NewFileName)))
            {
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);
            }

            if (filenameChanged && view.FileName.IsValidViewFile() && view.NewFileName.IsValidViewFile())
            {
                using (var streamWriter = new StreamWriter(HttpContext.Current.Request.MapPath("~/Views/" + view.NewFileName), false))
                {
                    streamWriter.WriteLine(ct); // Write the text
                }

                // If new file was successfully written, delete the old one
                // TODO: sync tree here?
                if (view.FileName.StartsWith("-1") == false && view.FileName.IsValidViewFile() && File.Exists(HttpContext.Current.Request.MapPath("~/Views/" + view.NewFileName)))
                {
                    File.Delete(HttpContext.Current.Request.MapPath("~/Views/" + view.FileName));
                }
            }

            if (view.FileName.IsValidViewFile())
            {
                var file = filenameChanged ? view.NewFileName : view.FileName;

                using (var streamWriter = new StreamWriter(HttpContext.Current.Request.MapPath("~/Views/" + file), false))
                {
                    streamWriter.WriteLine(ct); // Write the text
                }
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        }

        internal string GetLayout(string contents)
        {
            // Match on: 
            //		The word "Layout"
            //		then optional whitespace of any length
            //		until equals sign
            //		then optional whitespace of any length
            //		until 1 double quote
            //		then any characters (non greedy) - this is a group and will be used to get the value
            //		until 1 double quote
            //		then optional whitespace of any length	
            //		until 1 semicolon
            const string pattern = "Layout\\s*=\\s*\"{1}(.*?)\"{1}\\s*;";

            var match = Regex.Match(contents, pattern);
            var layout = match.Groups[1].Value.Trim();

            return layout == string.Empty ? null : layout;
        }

        internal List<Section> GetSections(string contents)
        {
            var allIndexOf = contents.AllIndexOf("@RenderSection", StringComparison.OrdinalIgnoreCase);
            var sections = new List<Section>();

            foreach (var index in allIndexOf)
            {
                var start = contents.Substring(index);
                var end = start.Substring(0, start.IndexOf(')'));
                var sectionPart = contents.Substring(index, end.Length + 2);

                var values = sectionPart.Replace("@RenderSection", string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty).Trim();
                var noParentheses = values.Substring(1, values.Length - 2).Trim();
                var splitValues = noParentheses.Split(',');
                var sectionName = splitValues[0].Replace("\"", string.Empty);

                // If no answer is specified then it defaults to true, else parse the actual value
                var sectionRequired = true;
                if (splitValues.Length > 1)
                    bool.TryParse(splitValues[1], out sectionRequired);

                sections.Add(new Section { Name = sectionName, Required = sectionRequired });
            }

            return sections;
        }
    }

    public class Section
    {
        public string Name { get; set; }
        public bool Required { get; set; }
    }

    public class ViewFolder
    {
        public string FolderName { get; set; }
        public string Parent { get; set; }
    }

    public class ViewFile
    {
        public string Value { get; set; }
        public string FileName { get; set; }
        public string NewFileName { get; set; }
        public string Parent { get; set; }
        public bool IsNew { get; set; }
        public string Layout { get; set; }
        public int MasterTemplateId { get; set; }
        public List<Section> Sections { get; set; }
    }

    public static class ViewExtensions
    {
        public static bool IsValidViewFile(this string path)
        {
            return path.EndsWith(".cshtml") && path.Contains("..") == false;
        }

        public static bool IsValidFolder(this string foldername)
        {
            return foldername.Contains("..") == false;
        }
    }

    public static class StringExtensions
    {
        public static IList<int> AllIndexOf(this string text, string str, StringComparison comparisonType)
        {
            IList<int> allIndexOf = new List<int>();
            int index = text.IndexOf(str, comparisonType);
            while (index != -1)
            {
                allIndexOf.Add(index);
                index = text.IndexOf(str, index + str.Length, comparisonType);
            }
            return allIndexOf;
        }
    }
}