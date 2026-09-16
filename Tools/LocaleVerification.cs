using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using AfterSeoul.Core;
using Newtonsoft.Json;
class LocaleVerification {
    static int checks;
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);checks++;}
    static Dictionary<string,string> Read(string p)=>JsonConvert.DeserializeObject<Dictionary<string,string>>(File.ReadAllText(p));
    static int Main(string[] args){try {
        string root=args[0];
        var en=Read(Path.Combine(root,"en.json"));
        var ko=Read(Path.Combine(root,"ko.json"));
        foreach(string lang in new[]{"ko","en","jp","zh","ru"}) {
            string json=File.ReadAllText(Path.Combine(root,lang+".json"));
            var table=Read(Path.Combine(root,lang+".json"));
            Loc.Load(lang,"{}","{}",json,File.ReadAllText(Path.Combine(root,"en.json")));
            foreach(var kv in en.Where(x=>!x.Key.StartsWith("_"))) {
                Check(table.ContainsKey(kv.Key),lang+" missing "+kv.Key);
                var tokens=Regex.Matches(kv.Value,@"\{(\d+)(?:[^}]*)\}").Cast<Match>().Select(m=>m.Value).OrderBy(x=>x).ToArray();
                var actual=Regex.Matches(table[kv.Key],@"\{(\d+)(?:[^}]*)\}").Cast<Match>().Select(m=>m.Value).OrderBy(x=>x).ToArray();
                Check(tokens.SequenceEqual(actual),lang+" mismatched format "+kv.Key);
                if(lang!="ko")Check(!Regex.IsMatch(table[kv.Key],"[가-힣]"),lang+" Korean leak "+kv.Key);
                int max=-1;foreach(Match m in Regex.Matches(kv.Value,@"\{(\d+)"))max=Math.Max(max,int.Parse(m.Groups[1].Value));
                var parameters=Enumerable.Repeat<object>(1234L,max+1).ToArray();
                foreach(Match m in Regex.Matches(kv.Value,@"\{(\d+):HH:mm\}")) parameters[int.Parse(m.Groups[1].Value)]=DateTime.UtcNow;
                string formatted=Loc.Get(kv.Key,parameters);
                Check(!string.IsNullOrWhiteSpace(formatted),lang+" empty "+kv.Key);
            }
            Check(Loc.Get("GUIDE_5_BODY").Contains(lang=="ru"?"50 000":"50,000") || lang=="zh" && Loc.Get("GUIDE_5_BODY").Contains("50,000"),"Starter reward translation "+lang);
        }
        foreach(var kv in ko.Where(x=>x.Key.StartsWith("DQ_")||x.Key.StartsWith("EVENT_")))Check(kv.Value!=kv.Key,"Korean dialogue lost "+kv.Key);
        Console.WriteLine("PASS locale resources: "+checks+" checks");return 0;
    }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
