using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Umbraco.Web;
using Umbraco.Core.Models;
using System.IO;
using System.Linq.Expressions;
using System.Reflection;
using System.Xml;
using System.Xml.XPath;
using System.Xml.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Umbraco.Core;

namespace ConsoleApplication1
{

    public static class CsvConverter
    {
        public static string ToCSV<T>(this IEnumerable<T> list)
        {
            var type = typeof(T);
            var props = type.GetProperties();

            //Setup expression constants
            var param = Expression.Parameter(type, "x");
            var doublequote = Expression.Constant("\"");
            var doublequoteescape = Expression.Constant("\"\"");
            var comma = Expression.Constant(",");

            //Convert all properties to strings, escape and enclose in double quotes
            var propq = (from prop in props
                         let tostringcall = Expression.Call(Expression.Property(param, prop), prop.ReflectedType.GetMethod("ToString", new Type[0]))
                         let replacecall = Expression.Call(tostringcall, typeof(string).GetMethod("Replace", new Type[] { typeof(String), typeof(String) }), doublequote, doublequoteescape)
                         select Expression.Call(typeof(string).GetMethod("Concat", new Type[] { typeof(String), typeof(String), typeof(String) }), doublequote, replacecall, doublequote)
                         ).ToArray();

            var concatLine = propq[0];
            for (int i = 1; i < propq.Length; i++)
                concatLine = Expression.Call(typeof(string).GetMethod("Concat", new Type[] { typeof(String), typeof(String), typeof(String) }), concatLine, comma, propq[i]);

            var method = Expression.Lambda<Func<T, String>>(concatLine, param).Compile();

            var header = String.Join(",", props.Select(p => p.Name).ToArray());

            return header + Environment.NewLine + String.Join(Environment.NewLine, list.Select(method).ToArray());
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            GetUrl();
        }


        public static void CreateCSVFromGenericList<T>(List<T> list, string csvNameWithExt)
        {
            if (list == null || list.Count == 0) return;

            //get type from 0th member
            Type t = list[0].GetType();
            string newLine = Environment.NewLine;

            using (var sw = new StreamWriter(csvNameWithExt))
            {
                //make a new instance of the class name we figured out to get its props
                object o = Activator.CreateInstance(t);
                //gets all properties
                PropertyInfo[] props = o.GetType().GetProperties();

                //foreach of the properties in class above, write out properties
                //this is the header row
                foreach (PropertyInfo pi in props)
                {
                    sw.Write(pi.Name.ToUpper() + ",");
                }
                sw.Write(newLine);

                //this acts as datarow
                foreach (T item in list)
                {
                    //this acts as datacolumn
                    foreach (PropertyInfo pi in props)
                    {
                        //this is the row+col intersection (the value)
                        string whatToWrite =
                            Convert.ToString(item.GetType()
                                                 .GetProperty(pi.Name)
                                                 .GetValue(item, null))
                                .Replace(',', ' ') + ',';

                        sw.Write(whatToWrite);

                    }
                    sw.Write(newLine);
                }
            }
        }

        private static void GetUrl()
        {
            using (var conn = new SqlConnection())
            {
                conn.ConnectionString =
                    "server=10.81.33.167;database=BeautyLove_Umbraco;user id=sqladmin;password=Password1234";
                conn.Open();

                var command = "; WITH PathXml AS (SELECT nodeId, cast([xml] as xml).query('data(//@urlName[1])').value('.', 'varchar(max)') AS Path FROM cmsContentXml x  " +
                    "JOIN umbracoNode n ON x.nodeId = n.id AND n.trashed = 0 AND n.level > 1)  " +
                    "SELECT t.dataNtext as PrimaryCategory, un.id, 'https://beautycrew.com.au/' + IsNull((SELECT  " +
                    "pl.Path + '/' FROM PathXml pl WHERE ',' + un.path + ',' LIKE '%,' + CAST(pl.nodeId AS VARCHAR(MAX)) + ',%' ORDER BY CHARINDEX(',' + CAST(pl.nodeId AS VARCHAR(MAX)) + ',',  " +
                    "',' + un.path + ',') FOR XML PATH('')), '') AS Url FROM umbracoNode un left outer join (select [cmsPropertyType].id, contentNodeId, dataNtext from [cmsPropertyData]  " +
                    "left outer join[cmsPropertyType] on[cmsPropertyType].id = [cmsPropertyData].propertytypeid left outer join[cmsDocument] on[cmsDocument].nodeId = [cmsPropertyData].contentNodeId  " +
                    "where [cmsPropertyData].contentNodeId = [cmsDocument].nodeId and [cmsPropertyData].contentNodeId in (select[cmsDocument].nodeId from[cmsDocument], [cmsContent]  " +
                    "where [cmsDocument].nodeId = [cmsContent].nodeId and [cmsContent].contentType in (1156)) and propertytypeid in (160) and[cmsPropertyType].id = [cmsPropertyData].propertytypeid  " +
                    " and[cmsDocument].newest = 1 and[cmsDocument].versionId = [cmsPropertyData].versionId and[cmsDocument].Published = 1) t on t.contentNodeId = un.id  " +
                    " WHERE nodeObjectType = 'C66BA18E-EAF3-4CFF-8A22-41B16D66A972'and (un.path like '%1136,1137%')  " +
                    " AND trashed = 0 ORDER BY 3";



                var adapter = new SqlDataAdapter(command, conn);

                var sku = new DataSet();
                adapter.Fill(sku, "umbracoNode");

                var contentList = sku.Tables[0].AsEnumerable().Select(dataRow => 
                    new Content() { id = dataRow.Field<int>("id"), primaryCategory = dataRow.Field<string>("PrimaryCategory"), url = dataRow.Field<string>("Url") }).ToList();

                var newContentList = new List<NewContent>();

                conn.Close();

                foreach (var content in contentList)
                {
                    if (content.primaryCategory != null)
                    {
                        var primaryCat = JArray.Parse(content.primaryCategory);

                        var category = primaryCat.Select(p => new Content()
                        {
                            primaryCategory = (string) p["key"],
                        }).ToList();


                        var url = GetUrlName(new Uri(content.url, UriKind.Absolute));

                        if (content.url.Contains("article-content"))
                        {
                            newContentList.Add(new NewContent() {url = content.url , newUrl = content.url.Replace(content.url, "https://beautycrew.com.au/" + category[0].primaryCategory + "/articles/" + url)});
                            //newContentList[0].url = content.url;
                            //newContentList[0].newUrl = content.url.Replace(content.url,
                            //    "https://beautycrew.com.au/" + category[0].primaryCategory + "/articles/" + url);
                        }
                        if (content.url.Contains("how-to-content"))
                        {
                            newContentList.Add(new NewContent() { url = content.url, newUrl = content.url.Replace(content.url, "https://beautycrew.com.au/" + category[0].primaryCategory + "/how-tos/" + url) });
                        }
                        if (content.url.Contains("gallery-content"))
                        {
                            newContentList.Add(new NewContent() { url = content.url, newUrl = content.url.Replace(content.url, "https://beautycrew.com.au/" + category[0].primaryCategory + "/galleries/" + url) });
                        }
                    }

                    
                }
                
                CreateCSVFromGenericList(newContentList, "test1.csv");
            }
        }

        private static string GetUrlName(Uri current)
        {
            var path = current.GetAbsolutePathDecoded();
            var urlParts = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            return urlParts[1]; //string.Concat('/', string.Join("/", urlParts.Take(2)), '/');
        }

        private class Content
        {
            public int id { get; set; }
            public string primaryCategory { get; set; }
            public string url { get; set; }
            public string newUrl { get; set; }
        }

        private class NewContent
        {
            public string url { get; set; }
            public string newUrl { get; set; }
        }
    }
}
