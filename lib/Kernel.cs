using System;
using System.Collections.Generic;
using System.Threading;
using System.Runtime.InteropServices;
namespace Niecza {
    // We like to reuse continuation objects for speed - every function only
    // creates one kind of continuation, but tweaks a field for exact return
    // point.  As such, call frames and continuations are in 1:1 correspondence
    // and are unified.  Functions take a current continuation and return a new
    // continuation; we tail recurse with trampolines.

    // Only call other functions in Continue, not in the CallableDelegate or
    // equivalent!
    public delegate Frame CallableDelegate(Frame caller,
            Variable[] pos, Dictionary<string, Variable> named);
    // Used by DynFrame to plug in code
    public delegate Frame DynBlockDelegate(Frame frame);

    public abstract class IP6 {
        public abstract DynMetaObject GetMO();
        public abstract Frame GetAttribute(Frame caller, string name);

        protected Frame Fail(Frame caller, string msg) {
            return Kernel.Die(caller, msg + " in class " + GetMO().name);
        }

        // Most reprs won't have a concept of type objects
        public virtual bool IsDefined() { return true; }

        // include the invocant in the positionals!  it will not usually be
        // this, rather a container of this
        public virtual Frame InvokeMethod(Frame caller, string name,
                Variable[] pos, Dictionary<string, Variable> named) {
            IP6 m;
            //Kernel.LogNameLookup(name);
            if (GetMO().mro_methods.TryGetValue(name, out m)) {
                return m.Invoke(caller, pos, named);
            }
            return Fail(caller, "Unable to resolve method " + name);
        }

        public virtual Frame HOW(Frame caller) {
            caller.resultSlot = GetMO().how;
            return caller;
        }

        public virtual IP6 GetTypeObject() {
            return GetMO().typeObject;
        }

        public virtual string GetTypeName() {
            return GetMO().name;
        }

        public virtual bool Isa(DynMetaObject mo) {
            return GetMO().HasMRO(mo);
        }

        public virtual bool Does(DynMetaObject mo) {
            return GetMO().HasMRO(mo);
        }

        public virtual Frame Invoke(Frame c, Variable[] p,
                Dictionary<string, Variable> n) {
            DynMetaObject.InvokeHandler ih = GetMO().mro_OnInvoke;
            if (ih != null) {
                return ih(this, c, p, n);
            } else {
                Variable[] np = new Variable[p.Length + 1];
                Array.Copy(p, 0, np, 1, p.Length);
                np[0] = Kernel.NewROScalar(this);
                return InvokeMethod(c, "INVOKE", np, n);
            }
        }
    }

    // A Variable is the meaning of function arguments, of any subexpression
    // except the targets of := and ::=.

    public abstract class Variable : IP6 {
        // these should be treated as ro for the life of the variable
        public bool rw;
        public bool islist;
        public IP6 whence;

        public abstract IP6  Fetch();
        public abstract void Store(IP6 v);

        public abstract IP6  GetVar();

        public override Frame GetAttribute(Frame c, string s) {
            return Kernel.Die(c, "Containers do not have attributes");
        }
        public override DynMetaObject GetMO() { return null; }
    }

    public sealed class SimpleVariable: Variable {
        IP6 val;

        public SimpleVariable(bool rw, bool islist, IP6 whence, IP6 val) {
            this.val = val; this.whence = whence; this.rw = rw;
            this.islist = islist;
        }

        public override IP6  Fetch()       { return val; }
        public override void Store(IP6 v)  {
            if (!rw) {
                throw new InvalidOperationException("Writing to readonly scalar");
            }
            val = v;
        }
        public override IP6  GetVar()      { return this; }

        public override DynMetaObject GetMO() { return Kernel.ScalarMO; }
    }

    // Used to make Variable sharing explicit in some cases; will eventually be
    // the only way to share a bvalue
    public sealed class BValue {
        public Variable v;
        public BValue(Variable v) { this.v = v; }
    }

    // This stores all the invariant stuff about a Sub, i.e. everything
    // except the outer pointer.  Now distinct from protopads
    public class SubInfo {
        public int[] lines;
        public DynBlockDelegate code;
        public DynMetaObject mo;
        // for inheriting hints
        public SubInfo outer;
        public string name;
        public Dictionary<string, object> hints;
        // maybe should be a hint
        public LAD ltm;

        // records: $start-ip, $end-ip, $type, $goto, $lid
        public const int ON_NEXT = 1;
        public const int ON_LAST = 2;
        public const int ON_REDO = 3;
        public const int ON_RETURN = 4;
        public const int ON_DIE = 5;
        public const int ON_SUCCEED = 6;
        public const int ON_PROCEED = 7;
        public const int ON_GOTO = 8;
        public int[] edata;
        public string[] label_names;

        public static string DescribeControl(int type, Frame tgt, int lid,
                string name) {
            string ty = (type == ON_RETURN) ? "return" :
                        (type == ON_REDO)   ? "redo" :
                        (type == ON_LAST)   ? "last" :
                        (type == ON_NEXT)   ? "next" : "unknown control";
            if (lid >= 0) {
                return ty + "(" + tgt.info.label_names[lid] + ", lexotic)";
            } else if (name != null) {
                return ty + "(" + name + ", dynamic)";
            } else {
                return ty;
            }
        }

        public int FindControlEnt(int ip, int ty, string name, int lid) {
            for (int i = 0; i < edata.Length; i+=5) {
                if (ip < edata[i] || ip >= edata[i+1])
                    continue;
                if (ty != edata[i+2])
                    continue;
                if (lid >= 0 && lid != edata[i+4])
                    continue;
                if (name != null && !name.Equals(label_names[edata[i+4]]))
                    continue;
                return edata[i+3];
            }
            return -1;
        }

        public void PutHint(string name, object val) {
            if (hints == null)
                hints = new Dictionary<string,object>();
            hints[name] = val;
        }

        public bool GetLocalHint<T>(string name, out T val) where T: class {
            object o;
            if (hints != null && hints.TryGetValue(name, out o)) {
                val = o as T;
                return true;
            } else {
                val = null;
                return false;
            }
        }

        public SubInfo(string name, int[] lines, DynBlockDelegate code,
                SubInfo outer, Dictionary<string,object> hints, LAD ltm,
                int[] edata, string[] label_names) {
            this.lines = lines;
            this.code = code;
            this.outer = outer;
            this.hints = hints;
            this.ltm = ltm;
            this.name = name;
            this.edata = edata;
            this.label_names = label_names;
        }

        public SubInfo(string name, DynBlockDelegate code) :
            this(name, null, code, null, null, null, new int[0], null) { }
    }

    // We need hashy frames available to properly handle BEGIN; for the time
    // being, all frames will be hashy for simplicity
    public class Frame: IP6 {
        public readonly Frame caller;
        public readonly Frame outer;
        public readonly SubInfo info;
        public object resultSlot = null;
        public int ip = 0;
        public readonly DynBlockDelegate code;
        public Dictionary<string, object> lex;
        // statistically, most subs have between 1 and 4 anonymous lexicals
        public object lex0;
        public object lex1;
        public object lex2;
        public object lex3;
        public object[] lexn;

        public RxFrame rx;

        public Variable[] pos;
        public Dictionary<string, Variable> named;

        public Frame(Frame caller_, Frame outer_,
                SubInfo info_) {
            caller = caller_;
            outer = outer_;
            code = info_.code;
            info = info_;
        }

        public Frame Continue() {
            return code(this);
        }

        public override Frame GetAttribute(Frame c, string name) {
            c.resultSlot = lex[name];
            return c;
        }

        public Variable ExtractNamed(string n) {
            Variable r;
            if (named != null && named.TryGetValue(n, out r)) {
                named.Remove(n);
                return r;
            } else {
                return null;
            }
        }

        public override DynMetaObject GetMO() { return Kernel.CallFrameMO; }

        public int ExecutingLine() {
            if (info != null && info.lines != null) {
                return ip >= info.lines.Length ? 0 : info.lines[ip];
            } else {
                return 0;
            }
        }

        public string ExecutingFile() {
            string l;
            SubInfo i = info;
            while (i != null) {
                // possibly, using $?FILE and Fetch would be better
                if (i.GetLocalHint("?file", out l))
                    return l;
                i = i.outer;
            }
            return "";
        }

        public Variable LexicalFind(string name) {
            Frame csr = this;
            while (csr != null) {
                object o;
                if (csr.lex == null) {
                    csr = csr.outer;
                    continue;
                }
                if (csr.lex.TryGetValue(name, out o))
                    return (Variable)o;
                csr = csr.outer;
            }
            return Kernel.NewROScalar(Kernel.AnyP);
        }

        private static List<string> spacey = new List<string>();
        public string DepthMark() {
            Frame f = this;
            int ix = 0;
            while (f != null) { ix++; f = f.caller; }
            while (spacey.Count <= ix) { spacey.Add(new String(' ', spacey.Count * 2)); }
            return spacey[ix];
        }
    }

    // NOT IP6; these things should only be exposed through a ClassHOW-like
    // façade
    public class DynMetaObject {
        public IP6 how;
        public IP6 typeObject;
        public string name;

        public LexerCache lexcache;
        public LexerCache GetLexerCache() {
            if (lexcache == null)
                lexcache = new LexerCache();
            return lexcache;
        }

        public delegate Frame InvokeHandler(IP6 th, Frame c,
                Variable[] pos, Dictionary<string, Variable> named);

        public InvokeHandler OnInvoke;

        public InvokeHandler mro_OnInvoke;
        public Dictionary<string, IP6> mro_methods;

        public List<DynMetaObject> superclasses
            = new List<DynMetaObject>();
        public Dictionary<string, IP6> local
            = new Dictionary<string, IP6>();
        public List<string> local_attr = new List<string>();

        public Dictionary<string, int> slotMap = new Dictionary<string, int>();
        public int nslots = 0;

        private WeakReference wr_this;
        // protected by static lock
        private HashSet<WeakReference> subclasses = new HashSet<WeakReference>();
        private static object mro_cache_lock = new object();

        public int FindSlot(string name) {
            //Kernel.LogNameLookup(name);
            return slotMap[name];
        }

        public Dictionary<string, List<DynObject>> multiregex;

        public DynMetaObject[] mro;
        public HashSet<DynMetaObject> isa;

        public DynMetaObject(string name) {
            this.name = name;
            this.wr_this = new WeakReference(this);

            isa = new HashSet<DynMetaObject>();
        }

        private void Revalidate() {
            mro_OnInvoke = null;
            mro_methods = new Dictionary<string,IP6>();

            if (mro == null)
                return;

            for (int kx = mro.Length - 1; kx >= 0; kx--) {
                DynMetaObject k = mro[kx];
                if (k.OnInvoke != null)
                    mro_OnInvoke = k.OnInvoke;

                foreach (KeyValuePair<string,IP6> m in k.local)
                    mro_methods[m.Key] = m.Value;
            }
        }

        private void SetMRO(DynMetaObject[] arr) {
            lock(mro_cache_lock) {
                if (mro != null)
                    foreach (DynMetaObject k in mro)
                        k.subclasses.Remove(wr_this);
                foreach (DynMetaObject k in arr)
                    k.subclasses.Add(wr_this);
            }
            mro = arr;
        }

        ~DynMetaObject() {
            lock(mro_cache_lock)
                if (mro != null)
                    foreach (DynMetaObject k in mro)
                        k.subclasses.Remove(wr_this);
        }

        private void Invalidate() {
            if (mro == null)
                return;
            List<DynMetaObject> notify = new List<DynMetaObject>();
            lock(mro_cache_lock)
                foreach (WeakReference k in subclasses)
                    notify.Add(k.Target as DynMetaObject);
            foreach (DynMetaObject k in notify)
                if (k != null)
                    k.Revalidate();
        }

        public void AddMultiRegex(string name, IP6 m) {
            if (multiregex == null)
                multiregex = new Dictionary<string, List<DynObject>>();
            List<DynObject> dl;
            if (! multiregex.TryGetValue(name, out dl)) {
                dl = new List<DynObject>();
                multiregex[name] = dl;
            }
            dl.Add((DynObject)m);
        }

        public IP6 Can(string name) {
            IP6 m;
            if (mro_methods.TryGetValue(name, out m))
                return m;
            return null;
        }

        public Dictionary<string,IP6> AllMethods() {
            return mro_methods;
        }

        public HashSet<IP6> AllMethodsSet() {
            HashSet<IP6> r = new HashSet<IP6>();
            foreach (KeyValuePair<string,IP6> kv in mro_methods)
                r.Add(kv.Value);
            return r;
        }

        public bool HasMRO(DynMetaObject m) {
            return isa.Contains(m);
        }

        private static bool C3Debug =
            Environment.GetEnvironmentVariable("NIECZA_C3_TRACE") != null;

        private static string MROStr(List<DynMetaObject> chain) {
            return Kernel.JoinS(" <- ", chain, delegate(DynMetaObject o) {
                return o.name;
            });
        }

        private static void DumpC3Lists(string f, DynMetaObject[] m,
                List<List<DynMetaObject>> d) {
            Console.WriteLine(f + MROStr(new List<DynMetaObject>(m)) + " // " +
                    Kernel.JoinS(" | ", d, MROStr));
        }

        public void AddMethod(string name, IP6 code) {
            local[name] = code;
            Invalidate();
        }

        public void AddAttribute(string name) {
            local_attr.Add(name);
            Invalidate();
        }

        public void AddSuperclass(DynMetaObject other) {
            superclasses.Add(other);
        }

        // this gets called more than once for Scalar, ClassHOW, and Sub
        // just be sure that the attrib list doesn't change
        public void Complete() {
            List<List<DynMetaObject>> toMerge = new List<List<DynMetaObject>>();
            List<DynMetaObject> mro_l = new List<DynMetaObject>();
            isa = new HashSet<DynMetaObject>();
            toMerge.Add(new List<DynMetaObject>());
            toMerge[0].Add(this);

            foreach (DynMetaObject dmo in superclasses) {
                toMerge[0].Add(dmo);
                toMerge.Add(new List<DynMetaObject>(dmo.mro));
            }

            if (C3Debug)
                DumpC3Lists("C3 start: " + name + ": ", mro, toMerge);

            while (true) {
top:
                if (C3Debug)
                    DumpC3Lists("C3 iter: ", mro, toMerge);

                foreach (List<DynMetaObject> h in toMerge) {
                    if (h.Count == 0) {
                        continue; // next CANDIDATE
                    }
                    DynMetaObject cand = h[0];
                    foreach (List<DynMetaObject> bs in toMerge) {
                        if (bs.Count == 0) {
                            continue; // next BLOCKER
                        }
                        if (bs[0] == cand) {
                            continue;
                        }
                        if (bs.Contains(cand)) {
                            goto blocked;
                        }
                    }
                    // no reason not to immediately put this, and by loop
                    // order the C3 condition is kept
                    mro_l.Add(cand);
                    isa.Add(cand);
                    foreach (List<DynMetaObject> l in toMerge) {
                        l.Remove(cand);
                    }
                    goto top;
blocked:
                    ;
                }
                if (C3Debug)
                    DumpC3Lists("C3 end: ", mro, toMerge);
                foreach (List<DynMetaObject> l in toMerge) {
                    if (l.Count != 0) {
                        // should refactor this to use a real p6exception
                        throw new Exception("C3 MRO inconsistency detected");
                    }
                }
                break;
            }

            SetMRO(mro_l.ToArray());

            nslots = 0;
            foreach (DynMetaObject k in mro) {
                foreach (string an in k.local_attr) {
                    slotMap[an] = nslots++;
                }
            }

            Invalidate();
        }
    }

    // This is quite similar to DynFrame and I wonder if I can unify them.
    // These are always hashy for the same reason as Frame above
    public class DynObject: IP6 {
        // the slots have to support non-containerized values, because
        // containers are objects now
        public object[] slots;
        public DynMetaObject klass;

        public DynObject(DynMetaObject klass) {
            this.klass = klass;
            this.slots = new object[klass.nslots];
        }

        public override DynMetaObject GetMO() { return klass; }

        public override Frame GetAttribute(Frame caller, string name) {
            if (slots == null) {
                return Fail(caller, "Attempted to access slot " + name +
                        " via an object with no slots");
            }
            caller.resultSlot = GetSlot(name);
            return caller;
        }

        public void SetSlot(string name, object obj) {
            slots[klass.FindSlot(name)] = obj;
        }

        public object GetSlot(string name) {
            return slots[klass.FindSlot(name)];
        }

        public override bool IsDefined() {
            return this != klass.typeObject;
        }
    }

    // This class is slated for bloody death.  See Kernel.BoxAny for the
    // replacement.
    public class CLRImportObject : IP6 {
        public readonly object val;

        public CLRImportObject(object val_) { val = val_; }

        public override Frame GetAttribute(Frame c, string nm) {
            return Kernel.Die(c, "Attribute " + nm +
                    " not available on CLRImportObject");
        }

        public override DynMetaObject GetMO() { return null; }
    }

    // A bunch of stuff which raises big circularity issues if done in the
    // setting itself.
    public class Kernel {
        public static DynBlockDelegate MainlineContinuation;

        // Note: for classes without public .new, there's no way to get
        // "interesting" user subclasses, so direct indexing is safe

        public static object UnboxDO(DynObject o) {
            return o.slots[0];
        }

        public static object UnboxAny(IP6 o) {
            // TODO: Check for compatibility?
            return UnboxDO((DynObject)o);
        }

        public static Stack<Frame> TakeReturnStack = new Stack<Frame>();

        public static Frame Take(Frame th, Variable payload) {
            Frame r = TakeReturnStack.Pop();
            r.lex["$*nextframe"] = NewROScalar(th);
            r.resultSlot = payload;
            th.resultSlot = payload;
            return r;
        }

        public static Frame CoTake(Frame th, Frame from) {
            TakeReturnStack.Push(th);
            return from;
        }

        public static Frame GatherHelper(Frame th, IP6 sub) {
            DynObject dyo = (DynObject) sub;
            Frame n = new Frame(th,
                                (Frame) dyo.slots[0],
                                (SubInfo) dyo.slots[1]);
            th.resultSlot = n;
            return th;
        }

        private static Frame SubInvoke(IP6 th, Frame caller,
                Variable[] pos, Dictionary<string,Variable> named) {
            DynObject dyo = ((DynObject) th);
            Frame outer = (Frame) dyo.slots[0];
            SubInfo info = (SubInfo) dyo.slots[1];

            Frame n = new Frame(caller, outer, info);
            n.pos = pos;
            n.named = named;

            return n;
        }
        private static SubInfo SubInvokeSubSI = new SubInfo("Sub.INVOKE", SubInvokeSubC);
        private static Frame SubInvokeSubC(Frame th) {
            Variable[] post;
            post = new Variable[th.pos.Length - 1];
            Array.Copy(th.pos, 1, post, 0, th.pos.Length - 1);
            return SubInvoke((DynObject)th.pos[0].Fetch(), th.caller,
                    post, th.named);
        }

        public static Frame Die(Frame caller, string msg) {
            DynObject n = new DynObject(((DynObject)StrP).klass);
            n.slots[0] = msg;
            return SearchForHandler(caller, SubInfo.ON_DIE, null, -1, null,
                    NewROScalar(n));
        }

        public static readonly DynMetaObject SubMO;
        public static readonly DynMetaObject ScalarMO;

        public static bool TraceCont;

        public static IP6 MakeSub(SubInfo info, Frame outer) {
            DynObject n = new DynObject(info.mo ?? SubMO);
            n.slots[0] = outer;
            n.slots[1] = info;
            return n;
        }

        public static DynObject MockBox(object v) {
            DynObject n = new DynObject(StrP.GetMO());
            n.slots[0] = v;
            return n;
        }

        public static Variable BoxAny(object v, IP6 proto) {
            if (v == null)
                return NewROScalar(proto);
            DynObject n = new DynObject(((DynObject)proto).klass);
            n.slots[0] = v;
            return NewROScalar(n);
        }

        // check whence before calling
        public static Frame Vivify(Frame th, Variable v) {
            IP6 w = v.whence;
            v.whence = null;
            return w.Invoke(th, new Variable[1] { v }, null);
        }

        private static SubInfo BindSI = new SubInfo("Bind/rw-viv", BindC);
        private static Frame BindC(Frame th) {
            switch (th.ip) {
                case 0:
                    th.ip = 1;
                    return Vivify(th, th.pos[0]);
                case 1:
                    return th.caller;
                default:
                    return Kernel.Die(th, "IP invalid");
            }
        }

        public static Frame NewBoundVar(Frame th, bool ro, bool islist,
                Variable rhs) {
            Frame n;
            if (islist) ro = true;
            if (!rhs.rw) ro = true;
            // fast path
            if (ro == !rhs.rw && islist == rhs.islist && rhs.whence == null) {
                th.resultSlot = rhs;
                return th;
            }
            // ro = true and rhs.rw = true OR
            // islist != rhs.islist OR
            // whence != null (and rhs.rw = true)

            if (!rhs.rw) {
                th.resultSlot = new SimpleVariable(false, islist, null,
                        rhs.Fetch());
                return th;
            }
            // ro = true and rhw.rw = true OR
            // whence != null
            if (ro) {
                th.resultSlot = new SimpleVariable(false, islist, null, rhs.Fetch());
                return th;
            }

            th.resultSlot = rhs;

            n = new Frame(th, null, BindSI);
            n.pos = new Variable[1] { rhs };
            return n;
        }

        // This isn't just a fetch and a store...
        private static SubInfo AssignSI = new SubInfo("Assign", AssignC);
        private static Frame AssignC(Frame th) {
            switch (th.ip) {
                case 0:
                    if (th.pos[0].whence == null)
                        goto case 1;
                    th.ip = 1;
                    return Vivify(th, th.pos[0]);
                case 1:
                    if (th.pos[0].islist) {
                        return th.pos[0].Fetch().InvokeMethod(th.caller,
                                "LISTSTORE", th.pos, null);
                    } else if (!th.pos[0].rw) {
                        return Kernel.Die(th.caller, "assigning to readonly value");
                    } else {
                        th.pos[0].Store(th.pos[1].Fetch());
                    }
                    return th.caller;
                default:
                    return Kernel.Die(th, "Invalid IP");
            }
        }

        public static Frame Assign(Frame th, Variable lhs, Variable rhs) {
            if (lhs.whence == null && !lhs.islist) {
                if (!lhs.rw) {
                    return Kernel.Die(th, "assigning to readonly value");
                }

                lhs.Store(rhs.Fetch());
                return th;
            }

            Frame n = new Frame(th, null, AssignSI);
            n.pos = new Variable[2] { lhs, rhs };
            return n;
        }

        // ro, not rebindable
        public static Variable NewROScalar(IP6 obj) {
            return new SimpleVariable(false, false, null, obj);
        }

        public static Variable NewRWScalar(IP6 obj) {
            return new SimpleVariable(true, false, null, obj);
        }

        public static Variable NewRWListVar(IP6 container) {
            return new SimpleVariable(false, true, null, container);
        }

        public static VarDeque SlurpyHelper(Frame th, int from) {
            VarDeque lv = new VarDeque();
            for (int i = from; i < th.pos.Length; i++) {
                lv.Push(th.pos[i]);
            }
            return lv;
        }

        public static Variable ContextHelper(Frame th, string name) {
            object rt;
            while (th != null) {
                if (th.lex == null) {
                    th = th.caller;
                    continue;
                }
                if (th.lex.TryGetValue(name, out rt)) {
                    return (Variable)rt;
                }
                th = th.caller;
            }
            name = name.Remove(1,1);
            Dictionary<string,BValue> gstash = (Dictionary<string,BValue>)
                (((CLRImportObject)GlobalO).val);
            BValue v;

            if (gstash.TryGetValue(name, out v)) {
                return v.v;
            } else {
                return PackageLookup(ProcessO, name).v;
            }
        }

        public static Variable DefaultNew(IP6 proto) {
            DynObject n = new DynObject(((DynObject)proto).klass);
            DynMetaObject[] mro = n.klass.mro;

            for (int i = mro.Length - 1; i >= 0; i--) {
                foreach (string s in mro[i].local_attr) {
                    n.SetSlot(s, NewRWScalar(AnyP));
                }
            }

            return NewROScalar(n);
        }

        public static Frame GetFirst(Frame th, IP6 lst) {
            DynObject dyl = lst as DynObject;
            if (dyl == null) goto slow;
            if (dyl.klass != ListMO) goto slow;
            VarDeque itemsl = (VarDeque) dyl.GetSlot("items");
            if (itemsl.Count() == 0) goto slow;
            th.resultSlot = itemsl[0];
            return th;

slow:
            return lst.InvokeMethod(th, "head", new Variable[] {
                    NewROScalar(lst) }, null);
        }

        public static DynMetaObject ListMO;
        public static IP6 AnyP;
        public static IP6 ArrayP;
        public static IP6 HashP;
        public static IP6 StrP;
        public static DynMetaObject CallFrameMO;

        public static BValue PackageLookup(IP6 parent, string name) {
            Dictionary<string,BValue> stash = (Dictionary<string,BValue>)
                (((CLRImportObject)parent).val);
            BValue v;

            if (stash.TryGetValue(name, out v)) {
                return v;
            } else if (name.EndsWith("::")) {
                Dictionary<string,BValue> newstash =
                    new Dictionary<string,BValue>();
                newstash["PARENT::"] = new BValue(NewROScalar(parent));
                return (stash[name] = new BValue(NewROScalar(
                            new CLRImportObject(newstash))));
            } else {
                // TODO: @foo, %foo
                return (stash[name] = new BValue(NewRWScalar(AnyP)));
            }
        }

        public static Frame StartP6Thread(Frame th, IP6 sub) {
            Thread thr = new Thread(delegate () {
                    Frame current = sub.Invoke(th, new Variable[0], null);
                    RunCore(current, th);
                });
            thr.Start();
            th.resultSlot = thr;
            return th;
        }

        public static void RunLoop(SubInfo boot) {
            Kernel.TraceCont = (Environment.GetEnvironmentVariable("NIECZA_TRACE") != null);
            RunCore(new Frame(null, null, boot), null);
        }

        public static void RunCore(Frame cur, Frame root) {
            while (cur != root) {
                try {
                    if (TraceCont) {
                        while (cur != root) {
                            System.Console.WriteLine("{0}|{1} @ {2}",
                                    cur.DepthMark(), cur.info.name, cur.ip);
                            cur = cur.code(cur);
                        }
                    } else {
                        while (cur != root)
                            cur = cur.code(cur);
                    }
                } catch (Exception ex) {
                    cur = Kernel.Die(cur, ex.ToString());
                }
            }
        }

        public static void AddMany(Dictionary<string,Variable> d1,
                Dictionary<string,Variable> d2) {
            foreach (KeyValuePair<string,Variable> kv in d2) {
                d1[kv.Key] = kv.Value;
            }
        }

        // XXX should be per-unit
        public static Variable Global;
        public static IP6 GlobalO;
        public static Variable Process;
        public static IP6 ProcessO;

        static Kernel() {
            DynMetaObject pStrMO = new DynMetaObject("protoStr");
            pStrMO.AddAttribute("value");
            pStrMO.Complete();
            StrP = new DynObject(pStrMO);

            SubMO = new DynMetaObject("Sub");
            SubMO.OnInvoke = new DynMetaObject.InvokeHandler(SubInvoke);
            SubMO.AddAttribute("outer");
            SubMO.AddAttribute("info");
            SubMO.Complete();
            SubMO.AddMethod("INVOKE", MakeSub(SubInvokeSubSI, null));

            ScalarMO = new DynMetaObject("Scalar");
            ScalarMO.AddAttribute("value");
            ScalarMO.Complete();

            GlobalO = new CLRImportObject(new Dictionary<string,BValue>());
            Global = NewROScalar(GlobalO);
            ProcessO = new CLRImportObject(new Dictionary<string,BValue>());
            Process = NewROScalar(ProcessO);
        }

        public static Dictionary<string, int> usedNames = new Dictionary<string, int>();
        public static void LogNameLookup(string name) {
            int k;
            usedNames.TryGetValue(name, out k);
            usedNames[name] = k + 1;
        }

        public static void DumpNameLog() {
            foreach (KeyValuePair<string, int> kv in usedNames)
                Console.WriteLine("{0} {1}", kv.Value, kv.Key);
        }

        // This is a library function in .NET 4
        public delegate string JoinSFormatter<T>(T x);
        public static string JoinS<T>(string sep, IEnumerable<T> things) {
            return JoinS(sep, things, delegate(T y) { return y.ToString(); });
        }
        public static string JoinS<T>(string sep, IEnumerable<T> things,
                JoinSFormatter<T> fmt) {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();

            bool fst = true;
            foreach (T x in things) {
                if (!fst) sb.Append(sep);
                fst = false;
                sb.Append(fmt(x));
            }
            return sb.ToString();
        }

        // exception processing goes in two stages
        // 1. find the correct place to unwind to, calling CATCH filters
        // 2. unwind, calling LEAVE functions
        public static Frame SearchForHandler(Frame th, int type, Frame tgt,
                int lid, string name, object payload) {
            // no CONTROL/CATCH yet, so we don't need to CPS the scanloop
            Frame csr;

            Frame unf = null;
            int unip = 0;

            for (csr = th; csr != null; csr = csr.caller) {
                // for lexoticism
                if (tgt != null && tgt != csr)
                    continue;
                unip = csr.info.FindControlEnt(csr.ip, type, name, lid);
                if (unip >= 0) {
                    unf = csr;
                    break;
                }
            }

            if (unf == null) {
                object mp = (type == SubInfo.ON_DIE) ? payload :
                    BoxAny("Unhandled control operator: " +
                            SubInfo.DescribeControl(type, tgt, lid, name), StrP);
                Frame r = new Frame(th, null, UnhandledSI);
                r.lex0 = mp;
                return r;
            } else {
                return Unwind(th, unf, unip, payload);
            }
        }

        private static SubInfo UnhandledSI = new SubInfo("Unhandled", UnhandledC);
        private static Frame UnhandledC(Frame th) {
            switch(th.ip) {
                case 0:
                    th.ip = 1;
                    return ((Variable)th.lex0).Fetch().InvokeMethod(th, "Str",
                            new Variable[1] { (Variable)th.lex0 }, null);
                case 1:
                    Console.Error.WriteLine("Unhandled exception: {0}",
                            (string) UnboxAny(((Variable)th.resultSlot).Fetch()));
                    th.lex0 = th.caller;
                    goto case 2;
                case 2:
                    if (th.lex0 == null)
                        Environment.Exit(1);
                    Console.Error.WriteLine("  at {0} line {1}",
                            ((Frame)th.lex0).ExecutingFile(),
                            ((Frame)th.lex0).ExecutingLine());
                    th.lex0 = ((Frame) th.lex0).caller;
                    goto case 2;
                default:
                    return Kernel.Die(th, "Invalid IP");
            }
        }

        public static Frame Unwind(Frame th, Frame tf, int tip, object td) {
            // LEAVE handlers aren't implemented yet.
            tf.ip = tip;
            tf.resultSlot = td;
            return tf;
        }
    }

    public sealed class VarDeque {
        private Variable[] data;
        private int head;
        private int count;

        public int Count() { return count; }

        public VarDeque() {
            data = new Variable[8];
        }

        public VarDeque(VarDeque tp) {
            data = (Variable[]) tp.data.Clone();
            head = tp.head;
            count = tp.count;
        }

        public VarDeque(Variable[] parcel) {
            int cap = 8;
            while (cap <= parcel.Length) cap *= 2;
            data = new Variable[cap];
            Array.Copy(parcel, 0, data, 0, parcel.Length);
            count = parcel.Length;
        }

        private int fixindex(int index) {
            int rix = index + head;
            if (rix >= data.Length) rix -= data.Length;
            return rix;
        }

        private int fixindexc(int index) {
            if (index >= count)
                throw new IndexOutOfRangeException();
            return fixindex(index);
        }

        public Variable this[int index] {
            get { return data[fixindexc(index)]; }
            set { data[fixindexc(index)] = value; }
        }

        public void Push(Variable vr) {
            checkgrow();
            data[fixindex(count++)] = vr;
        }

        public Variable Pop() {
            int index = fixindex(--count);
            Variable d = data[index];
            data[index] = null;
            return d;
        }

        public void Unshift(Variable vr) {
            checkgrow();
            head--;
            count++;
            if (head < 0) head += data.Length;
            data[head] = vr;
        }

        public void UnshiftN(Variable[] vrs) {
            for (int i = vrs.Length - 1; i >= 0; i--)
                Unshift(vrs[i]);
        }

        public Variable Shift() {
            int index = head++;
            if (head == data.Length) head = 0;
            count--;
            Variable d = data[index];
            data[index] = null;
            return d;
        }

        private void checkgrow() {
            if (count == data.Length - 1) {
                Variable[] ndata = new Variable[data.Length * 2];
                int z1 = data.Length - head;
                if (z1 >= count) {
                    Array.Copy(data, head, ndata, 0, count);
                } else {
                    Array.Copy(data, head, ndata, 0, z1);
                    int z2 = count - z1;
                    Array.Copy(data, 0, ndata, z1, z2);
                }
                data = ndata;
                head = 0;
            }
        }
    }
}

// The root setting
public class NULL {
    public static Niecza.Frame Environment = null;

    private static Niecza.SubInfo MAINSI = new Niecza.SubInfo("Null.MAIN", MAIN);
    public static Niecza.IP6 Installer = Niecza.Kernel.MakeSub(MAINSI, null);
    private static Niecza.Frame MAIN(Niecza.Frame th) {
        switch (th.ip) {
            default:
                return th.pos[0].Fetch().Invoke(th.caller,
                        new Niecza.Variable[0] {}, null);
        }
    }
    public static void Initialize() {}
}
