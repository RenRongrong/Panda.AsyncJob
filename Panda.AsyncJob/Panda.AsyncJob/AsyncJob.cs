using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Collections;
using System.Threading;

namespace PandaAsync
{   
    /// <summary>
    /// 处理多层次异步操作的类
    /// </summary>
    public class AsyncJob : INode
    {
        private ConcurrentQueue<Action> _outputQueue;
        private Task _inTask;
        private Task _outTask;
        private Circle<AsyncJob> _circle;
        private ConcurrentQueue<Action<AsyncJob>> _inputQueue;
        private bool _inputEnd;
        private AsyncJob _currentInputChild;
        private AsyncJob _currentOutputChild;
        private bool _inTaskCompleted;
        private bool _outTaskCompleted;

        /// <summary>
        /// 上一节点
        /// </summary>
        public INode Last { get; set; }

        /// <summary>
        /// 下一节点
        /// </summary>
        public INode Next { get; set; }

        /// <summary>
        /// 输出缓冲区长度
        /// </summary>
        public int OutBufferLength { get; set; }

        /// <summary>
        /// 输入缓冲区长度
        /// </summary>
        public int InBufferLength { get; set; }

        /// <summary>
        /// 该节点（包括所有下层）事件是否已经全部完成
        /// </summary>
        public bool IsCompleted
        {
            get
            {
                return this._inTaskCompleted && this._outTaskCompleted && this._outputQueue.Count == 0;
            }
        }

        /// <summary>
        /// 指示对异步工作的输入结束，该异步工作将在处理完剩余的输出后销毁。
        /// </summary>
        public void EndInput()
        {
            this._inputEnd = true;
        }

        public void In(Action<AsyncJob> act)
        {
            while(_inputQueue.Count >= InBufferLength) { Thread.Sleep(20); }
            this._inputQueue.Enqueue(act);
        }

        public void CallBack()
        {
            while(!this.IsCompleted)
            {
                Action act = this.PopCallBack();
                if(act != null)
                {
                    act();
                }
                else
                {
                    Thread.Sleep(20);
                }
            }
            //this.Dispose();
        }

        public void SendToChild(Action<AsyncJob> act)
        {
            if(this._circle.Count < 1)
            {
                throw new InvalidOperationException("没有子工作时无法使用SendToChild方法！");
            }
            if(this._currentInputChild == null)
            {
                this._currentInputChild = this._circle.Head;
            }
            this._currentInputChild.In(act);
            this._currentInputChild = this._currentInputChild.Next as AsyncJob;
        }

        /// <summary>
        /// 生成多层次异步操作的实例
        /// </summary>
        public AsyncJob()
        {
            this._inputQueue = new ConcurrentQueue<Action<AsyncJob>>();
            this._outputQueue = new ConcurrentQueue<Action>();
            this._circle = new Circle<AsyncJob>();
            this.OutBufferLength = 10;
            this.InBufferLength = 10;
            this.Last = null;
            this.Next = null;
            this._inputEnd = false;
            this._inTaskCompleted = false;
            this._outTaskCompleted = false;
            this._currentInputChild = _circle.Head;
        }

        public AsyncJob(Action<AsyncJob> act)
        {
            this._inputQueue = new ConcurrentQueue<Action<AsyncJob>>();
            this._outputQueue = new ConcurrentQueue<Action>();
            this._circle = new Circle<AsyncJob>();
            this.OutBufferLength = 10;
            this.InBufferLength = 10;
            this.Last = null;
            this.Next = null;
            this._inputEnd = false;
            this._inTaskCompleted = false;
            this._outTaskCompleted = false;
            this._currentInputChild = _circle.Head;
            this.In(act);
        }

        /// <summary>
        /// 添加子节点，所有的子节点以环形结构相连
        /// </summary>
        /// <param name="childJob"></param>
        public void addChild(AsyncJob childJob)
        {
            this._circle.Add(childJob);
        }

        /// <summary>
        /// 添加子节点，所有的子节点以环形结构相连
        /// </summary>
        public void addChild()
        {
            this.addChild(new AsyncJob());
        }

        /// <summary>
        /// 添加多个子节点，所有的子节点以环形结构相连
        /// </summary>
        /// <param name="childNum"></param>
        public void addChild(int childNum)
        {
            for(int i = 0; i < childNum; i++)
            {
                this.addChild(new AsyncJob());
            }
        }

        /// <summary>
        /// 发送回调信号，回调信号将进入缓冲区
        /// </summary>
        /// <param name="action"></param>
        public void SendCallBack(Action action)
        {
            while (this._outputQueue.Count >= this.OutBufferLength) { };
            this._outputQueue.Enqueue(action);
        }

        /// <summary>
        /// 从缓冲区中弹出回调函数，如缓冲区为空，则返回null
        /// </summary>
        /// <returns></returns>
        public Action PopCallBack()
        {
            Action act;
            if(this._outputQueue.TryDequeue(out act))
            {
                return act;
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// 启动事件（包括所有下层事件）
        /// </summary>
        public void Start()
        {
            _inTask = new Task(() =>
            {
                Action<AsyncJob> act;           
                while (!_inputEnd)
                {
                    if (this._inputQueue.TryDequeue(out act))
                    {
                        act(this);
                    }
                    else
                    {
                        Thread.Sleep(20);
                    }
                }
                if (this._circle.Count > 0)
                {
                    AsyncJob a = this._circle.Head;
                    a.In(j =>
                    {
                        j.EndInput();
                    });
                    while (a.Next != this._circle.Head)
                    {
                        a = a.Next as AsyncJob;
                        a.In(j =>
                        {
                            j.EndInput();
                        });
                    }
                }
                this._inTaskCompleted = true;
                
            });          
            _outTask = new Task(() =>
            {
                while (this._circle.Count > 0)
                {
                    if(this._currentOutputChild == null)
                    {
                        this._currentOutputChild = this._circle.Head;
                    }
                    Action act = _currentOutputChild.PopCallBack();
                    if (act != null)
                    {
                        this._outputQueue.Enqueue(act);
                        this._currentOutputChild = this._currentOutputChild.Next as AsyncJob;
                    }
                    else
                    {
                        if (this._currentOutputChild.IsCompleted)
                        {
                            this._circle.Remove(this._currentOutputChild);
                            this._currentOutputChild = this._currentOutputChild.Next as AsyncJob;
                        }
                        else
                        {
                            Thread.Sleep(20);
                            this._currentOutputChild = this._currentOutputChild.Next as AsyncJob;
                        }
                    }
                }
                this._outTaskCompleted = true;
            });
            _inTask.Start();
            _outTask.Start();
            foreach (AsyncJob e in this._circle)
            {
                e.Start();
            }
        }

        //public void Dispose()
        //{
        //    this._inTask.Dispose();
        //    this._outTask.Dispose();
        //}
    }

    /// <summary>
    /// 表示环形结构的类
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class Circle<T> : IEnumerable where T : class, INode
    {
        private T _current;
        private T _head;
        private int _count;

        /// <summary>
        /// 头结点
        /// </summary>
        public T Head
        {
            get { return this._head; }
        }

        /// <summary>
        /// 环中所有元素的数量
        /// </summary>
        public int Count
        {
            get { return this._count; }
        }

        /// <summary>
        /// 当前结点
        /// </summary>
        public T Current
        {
            get
            {
                if (this._current == null)
                {
                    this._current = this._head;
                }
                return this._current;
            }
        }

        /// <summary>
        /// 下一结点
        /// </summary>
        public void Next()
        {
            this._current = (T)this._current.Next;
        }

        /// <summary>
        /// 表示环形结构的类
        /// </summary>
        public Circle()
        {
            this._current = null;
            this._head = null;
            this._count = 0;
        }

        /// <summary>
        /// 在环尾添加新结点
        /// </summary>
        /// <param name="node"></param>
        public void Add(T node)
        {
            if(this._head == null)
            {
                this._head = node;
                node.Last = node;
                node.Next = node;
            }
            else
            {
                T tail = (T)this._head.Last;
                this._head.Last = node;
                node.Next = this._head;
                node.Last = tail;
                tail.Next = node;              
            }
            this._count++;
        }

        /// <summary>
        /// 移除结点
        /// </summary>
        /// <param name="node"></param>
        public void Remove(T node)
        {
            node.Last.Next = node.Next;
            node.Next.Last = node.Last;
            if(_head == node)
            {
                this._head = (T)node.Next;
            }
            if(_current == node)
            {
                this._current = (T)node.Next;
            }
            node = null;
            this._count--;
        }

        /// <summary>
        /// 获取迭代器
        /// </summary>
        /// <returns></returns>
        public IEnumerator GetEnumerator()
        {
            return new CirclEnumerator<T>(this);
        }

        private class CirclEnumerator<T2> : IEnumerator where T2 : class, INode
        {
            private Circle<T2> _circle;

            public CirclEnumerator(Circle<T2> circle)
            {
                _circle = circle;
            }

            public object Current
            {
                get
                {
                    return _circle._current;
                }
            }

            public bool MoveNext()
            {
                if(_circle.Count <= 0)
                {
                    return false;
                }
                if(_circle._current == _circle._head.Last)
                {
                    _circle._current = _circle._head;
                    return false;
                }
                else
                {
                    if (_circle._current == null)
                    {
                        _circle._current = _circle._head;
                    }
                    else
                    {
                        _circle._current = (T2)_circle._current.Next;
                    }
                    return true;
                }
            }

            public void Reset()
            {
                _circle._head = null;
                _circle._current = null;
                _circle._count = 0;
            }
        }
    }

    /// <summary>
    /// 环形结构的结点
    /// </summary>
    public interface INode
    {
        /// <summary>
        /// 上一结点
        /// </summary>
        INode Last { get; set; }
        /// <summary>
        /// 下一结点
        /// </summary>
        INode Next { get; set; }
    }
}
