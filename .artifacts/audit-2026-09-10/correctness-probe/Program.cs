using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Net;
using System.Net.Http;

const string assemblies = @"D:\Projects\bdo-grind-tracker\tests\BdoGrindTracker.App.Tests\bin\Release\net9.0-windows10.0.19041.0";
AssemblyLoadContext.Default.Resolving += (_, name) => {
    var file = Path.Combine(assemblies, name.Name + ".dll");
    return File.Exists(file) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(file) : null;
};
var tests = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(assemblies,"BdoGrindTracker.App.Tests.dll"));
var app = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(assemblies,"BdoGrindTracker.dll"));
var fixtureType = tests.GetType("BdoGrindTracker.App.Tests.TrackerSessionServiceTests+Fixture")!;
object Fixture(bool auto = false) => Activator.CreateInstance(fixtureType, BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance, null, new object?[]{auto, true, null, null, null}, null)!;
object Get(object o,string n) => o.GetType().GetProperty(n,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(o)!;
object? Call(object o,string n,params object?[] args) => o.GetType().GetMethod(n,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(o,args);
async Task Run(object o,string n,params object?[] args) => await (Task)Call(o,n,args)!;
async Task Process(object f,double minutes,int qty) => await Run(f,"ProcessAfter",TimeSpan.FromMinutes(minutes),new (string,int)[]{("Black Stone",qty)});
async Task Cleanup(object f) => await (ValueTask)Call(f,"DisposeAsync")!;

var historyType=app.GetType("BdoGrindTracker.App.Persistence.LootHistoryStore")!;
var historyDir=Path.Combine(Path.GetTempPath(),"Grindcrest-audit-history-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(historyDir);
var historyPath=Path.Combine(historyDir,"history.json");
var store=Activator.CreateInstance(historyType,new object[]{historyPath})!;
var prefix="{\"Version\":1,\"Entries\":[{\"SessionId\":\"11111111-1111-1111-1111-111111111111\",\"StartedAt\":\"2026-09-10T00:00:00Z\",\"UpdatedAt\":\"2026-09-10T01:00:00Z\",\"Duration\":\"01:00:00\",\"SpotId\":\"hermesia\",\"SilverBeforeTax\":0,\"SilverAfterTax\":0,\"SilverIsComplete\":true,\"Totals\":";
foreach(var (label,totals) in new[]{("NULL totals","null"),("case-colliding totals","{\"Black Stone\":1,\"black stone\":2}")}) {
    File.WriteAllText(historyPath,prefix+totals+"}]}");
    try { Call(store,"Load"); Console.WriteLine(label+": NO FAILURE"); }
    catch(TargetInvocationException ex) { Console.WriteLine(label+": "+ex.InnerException!.GetType().Name); }
}

var f1=Fixture();
try {
    Call(f1,"Begin"); await Process(f1,10,100); var s=Get(f1,"Service");
    Console.WriteLine("After 10m active: historyFileExists="+File.Exists(Path.Combine((string)Get(f1,"DirectoryPath"),"loot-history-v1.json")));
    await Run(s,"PauseAsync");
    Call(f1,"ResumeClocks"); await Process(f1,10,200);
    var blockedTemp=Path.Combine((string)Get(f1,"DirectoryPath"),"loot-history-v1.json.tmp");
    Directory.CreateDirectory(blockedTemp);
    await Run(s,"PauseAsync");
    Console.WriteLine("Blocked pause status="+Get(Get(s,"State"),"Status"));
    await Run(s,"NewSessionAsync");
    Console.WriteLine("Blocked new session status="+Get(Get(s,"State"),"Status")+"; HasSession="+Get(Get(s,"State"),"HasSession"));
    Directory.Delete(blockedTemp);
    await Run(s,"ShutdownAsync");
    var rows=(System.Collections.IEnumerable)Call(Get(f1,"HistoryStore"),"Load")!;
    foreach(var row in rows) Console.WriteLine("Persisted loot after clearing failed session="+JsonSerializer.Serialize(Get(row,"Totals")));
} finally { await Cleanup(f1); }

var f2=Fixture(true);
try {
    Call(f2,"Begin"); await Process(f2,60,100); var s=Get(f2,"Service");
    var response=new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
    fixtureType.GetProperty("Respond")!.SetValue(f2,(Func<Task<HttpResponseMessage>>)(()=>response.Task));
    var uploading=(Task)Call(s,"UploadHourlyToGarmothAsync")!;
    Console.WriteLine("Pending auto upload: IsBusy="+Get(Get(s,"State"),"IsBusy"));
    await Run(s,"PauseAsync");
    Console.WriteLine("After PauseAsync during auto upload: IsRunning="+Get(Get(s,"State"),"IsRunning"));
    response.SetResult(new HttpResponseMessage(HttpStatusCode.OK)); await uploading;
    await Process(f2,30,50); await Run(s,"PauseAsync");
    var oldId=Get(Get(s,"State"),"SessionId"); await Run(s,"NewSessionAsync"); await Run(s,"UploadHistoryAsync",oldId);
    Console.WriteLine("After 60m upload + 30m unsent + new session: Requests="+Get(Get(f2,"Requests"),"Count"));
} finally { await Cleanup(f2); }
Console.WriteLine("All replays used isolated temporary data and mocked HTTP/capture.");

var f3=Fixture();
try {
    Call(f3,"Begin"); await Process(f3,10,10); var s=Get(f3,"Service");
    await Run(s,"PauseAsync");
    var analyzer=Get(f3,"Analyzer");
    analyzer.GetType().GetProperty("IsAvailable")!.SetValue(analyzer,false);
    var id=Get(Get(s,"State"),"SessionId");
    await Run(s,"UpdateLootQuantityAsync",id,"Black Stone",15L,10L);
    Console.WriteLine("Correction1 under analyzer error: Total="+Get(Get(Get(s,"State"),"Loot"),"TotalQuantity")+"; IsError="+Get(Get(s,"State"),"IsError"));
    await Run(s,"UpdateLootQuantityAsync",id,"Black Stone",15L,10L);
    Console.WriteLine("Correction2 same stale original: Total="+Get(Get(Get(s,"State"),"Loot"),"TotalQuantity")+"; IsError="+Get(Get(s,"State"),"IsError"));
} finally { await Cleanup(f3); }

var f4=Fixture();
try {
    Call(f4,"Begin"); await Process(f4,10,100); var s=Get(f4,"Service");
    await Run(s,"PauseAsync");
    var blockedTemp=Path.Combine((string)Get(f4,"DirectoryPath"),"loot-history-v1.json.tmp");
    Directory.CreateDirectory(blockedTemp);
    await Run(s,"UploadAsync");
    Console.WriteLine("Upload with blocked local save: Requests="+Get(Get(f4,"Requests"),"Count")+"; Status="+Get(Get(s,"State"),"Status"));
    var rows=(System.Collections.IEnumerable)Call(Get(f4,"HistoryStore"),"Load")!;
    foreach(var row in rows) Console.WriteLine("Upload guard on disk despite remote success="+Get(row,"GarmothUploadBlocked"));
    Directory.Delete(blockedTemp);
} finally { await Cleanup(f4); }
File.WriteAllText(historyPath,prefix+"{\"Black Stone\":100}"+"}]}");
object? loadedWhileLocked;
using(var handle=new FileStream(historyPath,FileMode.Open,FileAccess.ReadWrite,FileShare.None)) {
    loadedWhileLocked=Call(store,"Load");
    Console.WriteLine("Transiently locked valid history: Loaded="+((System.Collections.IEnumerable)loadedWhileLocked!).Cast<object>().Count());
}
Call(store,"Save",loadedWhileLocked);
Console.WriteLine("After saving loader result: Entries="+((System.Collections.IEnumerable)Call(store,"Load")!).Cast<object>().Count());
