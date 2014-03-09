
//Gcode cleaner to work around some bugs in KISSlicer.

//This eliminates stuttering caused by too many extra G1 commands on
//straight lines or very short segments. It also reduces Gcode file
//size by up to 20%, without reducing print quality or resolution.

//To use this script automatically in KISSlicer, enter the following
//in the Printer -> Firmware tab under Post-Process:

///absolute/path/to/clean_gcode.exe "<FILE>"

//KISSlicer will replace the "<FILE>" part with the actual
//filename.gcode and this script will create a new file called
//filename.clean.gcode in the same folder.

//If you want to see an explanation for each removed G1 line:

//python clean_gcode.py filename.gcode --verbose
//colordiff -u filename.gcode filename.clean.gcode


using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Clean_gcode_cs
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.WriteLine("usage: clean_gcode.exe <filename> [--verbose]");
            }
            bool isVerbose = false;

            if (args.Length > 1 && args[1].Equals("--verbose", StringComparison.InvariantCultureIgnoreCase))
                isVerbose = true;

            LoadAndReWrite(args[0], isVerbose);
            Console.WriteLine("Press return to continue.");
            Console.Read();
        }

        public static bool LoadAndReWrite(string infilename, bool isVerbose)
        {
            if (!File.Exists(infilename))
            {
                Console.WriteLine("File {0} not found!", infilename);
                return false;
            }
            string outfilename = Path.GetDirectoryName(infilename) + @"\" +
                Path.GetFileNameWithoutExtension(infilename) + ".clean.gcode";
            try
            {
                Console.WriteLine("Opening file: " + infilename);
                using (TextReader infile = new StreamReader(File.OpenRead(infilename)))
                {
                    Console.WriteLine("Writing file: " + outfilename);
                    File.Delete(outfilename);
                    using (TextWriter outfile = new StreamWriter(File.OpenWrite(outfilename)))
                    {
                        rewrite(infile, outfile, isVerbose);
                    }
                }
                Console.WriteLine("Completed successfully.");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                return false;
            }
        }

        private static string should_skip(float[] p0, float[] p1, float[] p2)
        {
            //"Check if p1 is on a straight line between p0 and p2."
            if (p0 == null)
                return null;
            if (p1 == null)
                return null;
            if (p2 == null)
                return null;
            //# Calculate vectors for p1 and p2 relative to p0.
            float[] v1 = new float[p0.Length];
            for (int i = 0; i < p0.Length; i++)
            {
                v1[i] = p1[i] - p0[i];
            }
            float[] v2 = new float[p0.Length];
            for (int i = 0; i < p0.Length; i++)
            {
                v2[i] = p2[i] - p0[i];
            }
            //# Calculate the lengths of the relative vectors.

            double l1 = Math.Sqrt(v1.Select(x => x * x).Sum());
            double l2 = Math.Sqrt(v2.Select(x => x * x).Sum());

            if (l2 < 0.1)
                //# Ignore midpoint because the whole segment is very short.
                return string.Format("length={0:f2} (too short)", l2);
            double ratio = l1 / l2;
            //# Ratio of midpoint vs endpoint.
            // How far is the midpoint away from straight line?
            double[] d = new double[v1.Length];
            for (int i = 0; i < v1.Length; i++)
            {
                d[i] = v1[i] - v2[i] * ratio;//[v1[i] - v2[i]*ratio for i in indices];
            }

            double error = Math.Sqrt(d.Select(x => x * x).Sum());// sum(d[i]*d[i] for i in indices));
            if (error > 0.02)
                return null;
            // Ignore midpoint because it is very close to the straight line.
            return string.Format("ratio={0:f2} error={1:f2} (straight line)", ratio, error);
        }

        private static bool rewrite(TextReader infile, TextWriter outfile, bool verbose = false)
        {
            float[] p0 = null;
            float[] p1 = null;
            float[] p2 = null;
            string previous = null;
            string line = infile.ReadLine();
            Regex regx = new Regex(@"^G1 X([-\d\.]+) Y([-\d\.]+) E([-\d\.]+)$", RegexOptions.IgnoreCase);
            while (line != null)
            {
                Match match = regx.Match(line.Trim());

                if (match.Success)
                {
                    p2 = new float[match.Groups.Count - 1]; // the 0th group is the whole string?
                    for (int i = 1; i < match.Groups.Count; i++)
                    {
                        p2[i - 1] = float.Parse(match.Groups[i].Value);
                    }
                    //        p2 = [float(s) for s in match.groups()]
                    string message = should_skip(p0, p1, p2);

                    if (message != null)
                    {
                        //# Previous G1 is the midpoint of a straight line.
                        string stripped = previous.TrimEnd();
                        if (verbose)
                        {
                            //   # Prefix with ; to ignore this line when printing.
                            previous = string.Format(";{0} {1}", stripped, message);
                        }
                        else
                        {
                            previous = null;
                        }
                        p1 = p2;
                    }
                    else
                    {
                        p0 = p1;
                        p1 = p2;
                    }
                }
                else
                {
                    p0 = null;
                    p1 = null;
                }
                if (previous != null)
                    outfile.WriteLine(previous);
                previous = line;

                line = infile.ReadLine();
            }
            if (previous != null)
                outfile.WriteLine(previous);
            return true;
        }
    }
}
