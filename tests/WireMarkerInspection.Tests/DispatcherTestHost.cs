using System.Runtime.ExceptionServices;
using System.Windows.Threading;
using Xunit;

namespace WireMarkerInspection.Tests;

/// <summary>
/// Tests that drive a real acquisition loop each hold an STA thread plus thread-pool work. Running them
/// against each other starves the pool and makes pumped waits time out, so they share one serialized
/// collection.
/// </summary>
[CollectionDefinition(DispatcherTestHost.Collection,DisableParallelization=true)]
public sealed class DispatcherCollection;

/// <summary>
/// Runs view-model code the way the application does: on an STA thread whose dispatcher can be pumped.
/// Live camera frames and deferred selection restores are posted to that dispatcher, so a test that only
/// blocks its thread would deadlock or never observe them.
/// </summary>
internal static class DispatcherTestHost
{
    public const string Collection="dispatcher";

    public static void Sta(Action action)
    {
        Exception? failure=null;
        var thread=new Thread(()=>{try{action();}catch(Exception ex){failure=ex;}});
        thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();
        if(failure!=null)ExceptionDispatchInfo.Capture(failure).Throw();
    }

    public static void Sta(Func<Task> action)=>Sta(()=>action().GetAwaiter().GetResult());

    /// <summary>Runs queued dispatcher work until <paramref name="until"/> holds.</summary>
    public static void Pump(Func<bool> until,TimeSpan timeout,string what)
    {
        var deadline=DateTime.UtcNow+timeout;
        while(!until())
        {
            if(DateTime.UtcNow>deadline)throw new TimeoutException(what);
            var frame=new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>frame.Continue=false));
            Dispatcher.PushFrame(frame);
            Thread.Sleep(5);
        }
    }

    public static void Wait(Task task)
    {
        Pump(()=>task.IsCompleted,TimeSpan.FromSeconds(20),"A view-model operation did not complete.");
        task.GetAwaiter().GetResult();
    }
}
