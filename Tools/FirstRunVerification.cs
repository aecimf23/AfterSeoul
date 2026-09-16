using System;
using System.Collections.Generic;
using System.Reflection;
using AfterSeoul.Core;
class FirstRunVerification
{
    static int checks;
    static void Check(bool ok, string why) { if (!ok) throw new Exception(why); checks++; }
    sealed class Clock : IClock { public DateTimeOffset UtcNow => new DateTimeOffset(2026,9,16,0,0,0,TimeSpan.Zero); }
    sealed class Files : IFileStore {
        public Dictionary<string,string> Data = new Dictionary<string,string>();
        public bool Exists(string p)=>Data.ContainsKey(p);
        public string ReadAllText(string p)=>Data[p];
        public void WriteAllText(string p,string s)=>Data[p]=s;
        public void Replace(string s,string d){Data[d]=Data[s];Data.Remove(s);}
        public bool TryMove(string s,string d){if(!Exists(s))return false;Replace(s,d);return true;}
    }
    static int Main() {
        try {
            var field=typeof(GameSave).GetField("WelcomePage");
            Check(field!=null,"New-save briefing state must exist");
            var files=new Files(); var codec=new NewtonsoftJsonCodec();
            var service=new SaveService(files,codec,new Clock());
            Check((int)field.GetValue(new GameSave())==-1,"Old saves do not opt into a new tutorial");
            var save=service.LoadOrCreate();
            Check((int)field.GetValue(save)==0,"Missing save starts at first briefing page");
            field.SetValue(save,2); service.Save(save);
            Check((int)field.GetValue(service.LoadOrCreate())==2,"Interrupted briefing resumes its saved page");
            field.SetValue(save,-1);service.Save(save);
            Check((int)field.GetValue(service.LoadOrCreate())==-1,"Completed or skipped briefing never auto-replays");
            files.Data[SaveService.FileName]="{\"SchemaVersion\":2}";
            Check((int)field.GetValue(service.LoadOrCreate())==-1,"Legacy JSON without marker stays completed");
            files.Data[SaveService.FileName]="invalid json";
            Check((int)field.GetValue(service.LoadOrCreate())==0,"Recovered empty save receives briefing");
            Check(files.Exists(SaveService.FileName+".corrupt"),"Corrupt save retained");
            var method=typeof(Loc).GetMethod("Text");
            Check(method!=null,"UI translation API exists");
            Loc.Load("jp","{}","{}","{\"물자 {0:N0}\":\"物資 {0:N0}\"}","{\"물자 {0:N0}\":\"Supplies {0:N0}\",\"누락\":\"Fallback\"}");
            Check((string)method.Invoke(null,new object[]{"물자 {0:N0}",new object[]{1000}})==string.Format("物資 {0:N0}",1000),"Translated formatting preserves values");
            Check((string)method.Invoke(null,new object[]{"누락",null})=="Fallback","Missing locale key falls back to English");
            Loc.Load("ko","{}","{}",null,"{\"누락\":\"Fallback\"}");
            Check((string)method.Invoke(null,new object[]{"누락",null})=="누락","Korean source remains readable without overlay");
            Check((string)method.Invoke(null,new object[]{"{unknown}",null})=="{unknown}","Literal braces without args are safe");
            Console.WriteLine("PASS first-run/localization: "+checks+" checks");return 0;
        } catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
}
