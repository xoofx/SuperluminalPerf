using System;
using System.Threading;

namespace SuperluminalPerfRunner
{
    class Program
    {
        static void Main(string[] args)
        {
            SuperluminalPerf.Initialize();
            SuperluminalPerf.SetCurrentThreadName("Hello!");
            SuperluminalPerf.BeginEvent("MyMarker");
            Console.WriteLine("Hello World! Wait for 100ms");
            Thread.Sleep(100);
            SuperluminalPerf.EndEvent();
        }
    }
}
