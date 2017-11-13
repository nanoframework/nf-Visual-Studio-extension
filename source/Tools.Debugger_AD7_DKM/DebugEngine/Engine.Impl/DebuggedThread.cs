using System;

namespace nanoFramework.Tools.VisualStudio.DebugEngine
{
    public class DebuggedThread
    {
        public DebuggedThread(int id, nanoFramework.Tools.VisualStudio.DebugEngine.AD7Engine engine)
        {
            Id = id;
            Name = "";
            TargetId = (uint)id;
            AD7Thread ad7Thread = new nanoFramework.Tools.VisualStudio.DebugEngine.AD7Thread(engine, this);
            Client = ad7Thread;
            ChildThread = false;
        }

        public int Id { get; private set; }
        public uint TargetId { get; set; }
        public Object Client { get; private set; }      // really AD7Thread
        public bool Alive { get; set; }
        public bool Default { get; set; }
        public string Name { get; set; }
        public bool ChildThread { get; set; }       // transient child thread, don't inform UI of this thread
    }
}
