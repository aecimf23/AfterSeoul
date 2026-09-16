using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework.Api;
using NUnit.Framework.Internal;
using NUnit.Framework.Internal.Filters;

class MailRegression
{
    static int Main()
    {
        var runner=new NUnitTestAssemblyRunner(new DefaultTestAssemblyBuilder());
        runner.Load(Assembly.GetExecutingAssembly(),new Dictionary<string,object>());
        var result=runner.Run(TestListener.NULL,new FullNameFilter("AfterSeoul.Tests.OutboxTests"));
        Console.WriteLine(result.ToXml(true).OuterXml);
        return result.FailCount==0 && result.PassCount>0 ? 0 : 1;
    }
}
