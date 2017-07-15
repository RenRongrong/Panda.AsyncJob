using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using PandaAsync;

namespace Test
{
    class Program
    {
        static void Main(string[] args)
        {
            object locker = new object();
            int m = 0;
            int n = 0;
            int k = 0;
            AsyncJob job = new AsyncJob();
            job.addChild(10);
            job.Start();
            job.In((j) =>
            {
                for (int i = 0; i < 1000; i++)
                {
                    Console.WriteLine("执行主任务{0}", m++);
                    j.SendToChild((c) =>
                    {
                        lock (locker)
                        {
                            Console.WriteLine("执行子任务{0}", k++);
                        }
                        c.SendCallBack(() =>
                        {
                            Console.WriteLine("子任务回调{0}", n++);
                        });
                    });
                }
                j.EndInput();
            });
            job.CallBack();
            Console.WriteLine("任务结束！");
            Console.ReadLine();
        }
    }
}
