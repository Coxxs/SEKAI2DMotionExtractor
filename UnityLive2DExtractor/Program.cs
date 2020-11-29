using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using AssetStudio;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace UnityLive2DExtractor
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length != 1)
                return;
            if (!Directory.Exists(args[0]))
                return;
            Console.WriteLine($"Loading...");
            var assetsManager = new AssetsManager();
            assetsManager.LoadFolder(args[0]);
            if (assetsManager.assetsFileList.Count == 0)
                return;
            var containers = new Dictionary<AssetStudio.Object, string>();
            var cubismMocs = new List<MonoBehaviour>();

            var paramDb = new List<String> { };
            var partsDb = new List<String> { };

            // Some motion asset uses parameters not exists in models
            // Hardcoded these parameters here, but these parameters still won't work in models
            // Just try to better recover the original motion3.json
            paramDb.Add("PARAM_ARM_R_10");
            paramDb.Add("PARAM_ARM_L_10");

            foreach (var assetsFile in assetsManager.assetsFileList)
            {
                foreach (var asset in assetsFile.Objects)
                {
                    switch (asset)
                    {
                        case TextAsset m_TextAsset:
                            if (m_TextAsset.m_Name.EndsWith(".moc3"))
                            {
                                var asciiStr = System.Text.Encoding.ASCII.GetString(m_TextAsset.m_Script);

                                // Dirty way to find parameters in .moc3
                                var matches = Regex.Matches(asciiStr, "\0(Param[a-zA-Z0-9-_]*)\0", RegexOptions.IgnoreCase);
                                foreach (Match match in matches)
                                {
                                    var name = match.Groups[1].Value;
                                    paramDb.Add(name);

                                    // Again, Some motion asset uses parameters in wrong forms (CamelCase or UnderscoreCase)
                                    // Even if we recover these parameters, they still won't work in models
                                    // Just try to better recover the original motion3.json

                                    if (name.StartsWith("Param")) paramDb.Add(ToUnderscoreCase(match.Groups[1].Value));
                                    if (name.StartsWith("PARAM")) paramDb.Add(ToCamelCase(match.Groups[1].Value));
                                }

                                matches = Regex.Matches(asciiStr, "\0(Part[a-zA-Z0-9-_]*)\0", RegexOptions.IgnoreCase);
                                foreach (Match match in matches)
                                {
                                    var name = match.Groups[1].Value;
                                    partsDb.Add(name);
                                    if (name.StartsWith("Part")) partsDb.Add(ToUnderscoreCase(match.Groups[1].Value));
                                    if (name.StartsWith("PART")) partsDb.Add(ToCamelCase(match.Groups[1].Value));
                                }
                            }
                            break;
                        case AssetBundle m_AssetBundle:
                            foreach (var m_Container in m_AssetBundle.m_Container)
                            {
                                var preloadIndex = m_Container.Value.preloadIndex;
                                var preloadSize = m_Container.Value.preloadSize;
                                var preloadEnd = preloadIndex + preloadSize;
                                for (int k = preloadIndex; k < preloadEnd; k++)
                                {
                                    var pptr = m_AssetBundle.m_PreloadTable[k];
                                    if (pptr.TryGet(out var obj))
                                    {
                                        containers[obj] = m_Container.Key;
                                    }
                                }
                            }
                            break;
                    }
                }
            }

            paramDb = paramDb.Distinct().ToList();
            partsDb = partsDb.Distinct().ToList();

            var motionContainerKeyword = "/live2d/motion";
            foreach (KeyValuePair<AssetStudio.Object, string> container in containers)
            {
                var asset = container.Key;
                var cointainerPath = container.Value;

                if (!cointainerPath.Contains(motionContainerKeyword))
                {
                    continue;
                }

                switch (asset)
                {
                    case AnimationClip m_AnimationClip:
                        var animationClips = new List<AnimationClip>();
                        Console.WriteLine(cointainerPath);
                        animationClips.Add(m_AnimationClip);

                        //motion
                        var motions = new List<string>();
                        var converter = new CubismMotion3Converter2(paramDb, partsDb, animationClips.ToArray());
                        foreach (ImportedKeyframedAnimation animation in converter.AnimationList)
                        {
                            var json = new CubismMotion3Json
                            {
                                Version = 3,
                                Meta = new CubismMotion3Json.SerializableMeta
                                {
                                    Duration = animation.Duration,
                                    Fps = animation.SampleRate,
                                    Loop = true,
                                    AreBeziersRestricted = true,
                                    CurveCount = animation.TrackList.Count,
                                    UserDataCount = animation.Events.Count
                                },
                                Curves = new CubismMotion3Json.SerializableCurve[animation.TrackList.Count]
                            };
                            int totalSegmentCount = 1;
                            int totalPointCount = 1;
                            for (int i = 0; i < animation.TrackList.Count; i++)
                            {
                                var track = animation.TrackList[i];
                                json.Curves[i] = new CubismMotion3Json.SerializableCurve
                                {
                                    Target = track.Target,
                                    Id = track.Name,
                                    Segments = new List<float> { 0f, track.Curve[0].value }
                                };
                                for (var j = 1; j < track.Curve.Count; j++)
                                {
                                    var curve = track.Curve[j];
                                    var preCurve = track.Curve[j - 1];
                                    if (Math.Abs(curve.time - preCurve.time - 0.01f) < 0.0001f) //InverseSteppedSegment
                                    {
                                        var nextCurve = track.Curve[j + 1];
                                        if (nextCurve.value == curve.value)
                                        {
                                            json.Curves[i].Segments.Add(3f);
                                            json.Curves[i].Segments.Add(nextCurve.time);
                                            json.Curves[i].Segments.Add(nextCurve.value);
                                            j += 1;
                                            totalPointCount += 1;
                                            totalSegmentCount++;
                                            continue;
                                        }
                                    }
                                    if (float.IsPositiveInfinity(curve.inSlope)) //SteppedSegment
                                    {
                                        json.Curves[i].Segments.Add(2f);
                                        json.Curves[i].Segments.Add(curve.time);
                                        json.Curves[i].Segments.Add(curve.value);
                                        totalPointCount += 1;
                                    }
                                    else if (preCurve.outSlope == 0f && Math.Abs(curve.inSlope) < 0.0001f) //LinearSegment
                                    {
                                        json.Curves[i].Segments.Add(0f);
                                        json.Curves[i].Segments.Add(curve.time);
                                        json.Curves[i].Segments.Add(curve.value);
                                        totalPointCount += 1;
                                    }
                                    else //BezierSegment
                                    {
                                        var tangentLength = (curve.time - preCurve.time) / 3f;
                                        json.Curves[i].Segments.Add(1f);
                                        json.Curves[i].Segments.Add(preCurve.time + tangentLength);
                                        json.Curves[i].Segments.Add(preCurve.outSlope * tangentLength + preCurve.value);
                                        json.Curves[i].Segments.Add(curve.time - tangentLength);
                                        json.Curves[i].Segments.Add(curve.value - curve.inSlope * tangentLength);
                                        json.Curves[i].Segments.Add(curve.time);
                                        json.Curves[i].Segments.Add(curve.value);
                                        totalPointCount += 3;
                                    }
                                    totalSegmentCount++;
                                }
                            }
                            json.Meta.TotalSegmentCount = totalSegmentCount;
                            json.Meta.TotalPointCount = totalPointCount;

                            json.UserData = new CubismMotion3Json.SerializableUserData[animation.Events.Count];
                            var totalUserDataSize = 0;
                            for (var i = 0; i < animation.Events.Count; i++)
                            {
                                var @event = animation.Events[i];
                                json.UserData[i] = new CubismMotion3Json.SerializableUserData
                                {
                                    Time = @event.time,
                                    Value = @event.value
                                };
                                totalUserDataSize += @event.value.Length;
                            }
                            json.Meta.TotalUserDataSize = totalUserDataSize;

                            motions.Add($"motions/{animation.Name}.motion3.json");

                            var baseDestPath = Path.Combine(Path.GetDirectoryName(args[0]), "Live2DOutput");
                            var elementPath = cointainerPath.Substring(0, cointainerPath.LastIndexOf("/"));
                            var outputPath = Path.Combine(baseDestPath, elementPath);
                            Directory.CreateDirectory(outputPath);
                            File.WriteAllText($"{outputPath}/{animation.Name}.motion3.json", JsonConvert.SerializeObject(json, Formatting.Indented, new MyJsonConverter()));
                            Console.WriteLine($"{outputPath}/{animation.Name}.motion3.json");
                        }
                        break;
                }
            }
            Console.Read();
        }

        public static string ToUnderscoreCase(string str)
        {
            return string.Concat(str.Select((x, i) => i > 0 && char.IsUpper(x) ? "_" + x.ToString() : x.ToString())).ToUpper();
        }
        public static string ToCamelCase(string str)
        {
            return string.Join("", 
                str.Split('_')
                   .Select(i => i.Length > 0 ? char.ToUpper(i[0]) + i.Substring(1).ToLower() : "" )
            );
        }
    }
}
