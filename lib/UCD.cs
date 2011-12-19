using System;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.IO;
using Niecza;
using Niecza.UCD;

// Subsystem of Niecza that access the Unicode Character Database
//
// UCD data within Niecza is represented as a collection of named
// tables.  Most of these are property tables, which associate a
// character to one or two strings.
//
// Tables are identified by names.  Non-property tables never have
// names not starting with !.
namespace Niecza.UCD {
    abstract class Property {
        public abstract int[] GetRanges(Variable filter);
        public abstract string GetValue(int cp);

        protected static bool DoMatch(string value, Variable filter) {
            Variable r = Kernel.RunInferior(filter.Fetch().InvokeMethod(
                Kernel.GetInferiorRoot(), "ACCEPTS",
                new Variable[] { filter, Builtins.MakeStr(value) }, null));
            return r.Fetch().mo.mro_raw_Bool.Get(r);
        }
    }

    class LimitedProperty : Property {
        int[] data;
        string[][] values;

        public LimitedProperty(int[] data, string[][] values) {
            this.data = data;
            this.values = values;
        }

        public override string GetValue(int cp) {
            int lix = 0;
            int hix = data.Length / 2;

            while (true) {
                if ((hix - lix) <= 1) {
                    return values[data[lix*2+1]][0];
                }
                int mix = (lix + hix) / 2;
                if (cp >= data[mix*2]) {
                    lix = mix;
                } else {
                    hix = mix;
                }
            }
        }

        public override int[] GetRanges(Variable filter) {
            bool[] cfilter = new bool[values.Length];
            for (int i = 0; i < values.Length; i++) {
                foreach (string s in values[i]) {
                    if (DoMatch(s, filter)) {
                        cfilter[i] = true;
                        break;
                    }
                }
            }

            List<int> res = new List<int>();
            for (int i = 0; i < data.Length; i += 2) {
                if (cfilter[data[i+1]]) {
                    int upto = (i+2 == data.Length) ? 0x110000 : data[i+2];
                    if (res.Count > 0 && res[res.Count-1] == data[i]) {
                        res[res.Count-1] = upto;
                    } else {
                        res.Add(data[i]);
                        res.Add(upto);
                    }
                }
            }

            return res.ToArray();
        }
    }

    static class DataSet {
        static Dictionary<string,object> cache;
        static byte[] bits;
        static Dictionary<string,int[]> directory;
        static Dictionary<string,string> aliases;
        static bool Trace;

        const int FILES = 4;
        static int Int(ref int from) {
            from += 4;
            return (bits[from-4] << 24) | (bits[from-3] << 16) |
                (bits[from-2] << 8) | (bits[from-1]);
        }

        static uint BER(ref int from) {
            uint buf = 0;
            while (true) {
                byte inp = bits[from++];
                buf = (buf << 7) | (uint)(inp & 127);
                if ((inp & 128) == 0)
                    return buf;
            }
        }

        static string AsciiZ(ref int from) {
            int to = from;
            while (bits[to] != 0) to++;
            char[] buf = new char[to - from];
            for (int i = from; i < to; i++)
                buf[i - from] = (char)bits[i];
            from = to+1;
            return new string(buf);
        }

        static void InflateDirectory() {
            directory = new Dictionary<string,int[]>();
            Trace = Environment.GetEnvironmentVariable("NIECZA_UCD_TRACE") != null;
            if (Trace) Console.WriteLine("Unpacking directory ...");

            int rpos = 0;

            int[] fstart = new int[FILES];
            for (int i = 0; i < FILES; i++)
                fstart[i] = (i == 0 ? 20 : fstart[i-1]) + Int(ref rpos);
            rpos += 4; // skip the extra length

            int dend = fstart[0];

            while (rpos < dend) {
                int rpos0 = rpos;
                int[] loc = new int[FILES * 2];
                string name = AsciiZ(ref rpos);
                int nfiles = bits[rpos++];
                for (int i = 0; i < nfiles; i++) {
                    loc[2*i] = fstart[i];
                    fstart[i] += Int(ref rpos);
                    loc[2*i+1] = fstart[i];
                }
                if (Trace)
                    Console.WriteLine("Entry {0} (d.e. {1}): {2}",
                            name, rpos0, Kernel.JoinS(", ", loc));
                directory[name] = loc;
            }
            if (Trace) Console.WriteLine("done.");
        }

        static void InflateAliases() {
            int[] loc = directory["!PropertyAlias"];
            aliases = new Dictionary<string, string>();

            int rpos = loc[2];
            while (rpos < loc[3]) {
                string main = AsciiZ(ref rpos);
                string alias;
                while ((alias = AsciiZ(ref rpos)).Length != 0) {
                    aliases[alias] = main;
                    if (Trace) Console.WriteLine("Alias {0} -> {1}", alias, main);
                }
            }
        }

        static object InflateBinary(int[] loc) {
            List<int> vec = new List<int>();
            int rpos = loc[2];
            int last = 0;
            int ntyp = 1;
            while (rpos < loc[3]) {
                last += (int)BER(ref rpos);
                vec.Add(last);
                vec.Add(ntyp);
                ntyp = 1 - ntyp;
            }
            return new LimitedProperty(vec.ToArray(), new string[][] {
                    new string[] { "N" }, new string[] { "Y" } });
        }

        static object InflateEnum(int[] loc) {
            List<string[]> names = new List<string[]>();
            int rpos2 = loc[6];
            while (rpos2 < loc[7]) {
                names.Add(new string[] { AsciiZ(ref rpos2) });
            }
            int rpos0 = loc[2];
            int rpos1 = loc[4];
            int[] data = new int[(loc[5] - loc[4]) * 2];
            int lix = 0;
            for (int i = 0; i < data.Length / 2; i++) {
                lix += (int)BER(ref rpos0);
                data[2*i] = lix;
                data[2*i+1] = bits[rpos1++];
                //if (Trace) Console.WriteLine("inflate: {0} = {1}", data[2*i], names[data[2*i+1]][0]);
            }

            return new LimitedProperty(data, names.ToArray());
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public static object GetTable(string name) {
            if (cache == null)
                cache = new Dictionary<string,object>();
            if (bits == null) {
                bits = File.ReadAllBytes("unidata");
                InflateDirectory();
                InflateAliases();
            }

            object r;
            string a;
            if (aliases.TryGetValue(name, out a))
                name = a;
            if (cache.TryGetValue(name, out r))
                return r;


            int[] loc;
            if (!directory.TryGetValue(name, out loc))
                throw new NieczaException(name + " does not exist as a UCD table");

            switch (bits[loc[0]]) {
                case (byte)'B':
                    r = InflateBinary(loc);
                    break;
                case (byte)'E':
                    r = InflateEnum(loc);
                    break;
                default:
                    throw new NieczaException("Unhandled type code " + (char)bits[loc[0]]);
            }

            cache[name] = r;
            return r;
        }
    }
}

public partial class Builtins {
    public static Variable ucd_get_ranges(Variable tbl, Variable sm) {
        Property p = (Property)DataSet.GetTable(
                tbl.Fetch().mo.mro_raw_Str.Get(tbl));
        int[] rranges = p.GetRanges(sm);
        Variable[] cranges = new Variable[rranges.Length];
        for (int i = 0; i < rranges.Length; i++)
            cranges[i] = Builtins.MakeInt(rranges[i]);
        return Builtins.MakeParcel(cranges);
    }

    public static Variable ucd_get_value(Variable tbl, Variable ch) {
        Property p = (Property)DataSet.GetTable(
                tbl.Fetch().mo.mro_raw_Str.Get(tbl));
        return MakeStr(p.GetValue(
                (int) ch.Fetch().mo.mro_raw_Numeric.Get(ch)));
    }
}