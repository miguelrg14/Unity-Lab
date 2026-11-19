using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityLab.Racing.Online;
using UnityLab.Racing.Race;
using UnityLab.RTS.Army;
using UnityLab.RTS.Units;
using UnityLab.Shared;

namespace UnityLab.SmokeTest
{
    /// <summary>
    /// End-to-end check of a built player, used by CI. Started with command-line arguments:
    ///   -smoke-test &lt;output folder&gt;      enable; screenshots and smoke-results.txt go there
    ///   -smoke-only menu,racing,rts,online  run only these steps (default: menu,racing,rts)
    ///   -smoke-server host:port              server for the online step
    ///   -smoke-expect-remote N               the online step passes only once N other cars appear
    /// Every demo is played automatically (cars on autopilot, both RTS armies run by the AI) and
    /// the run exits with code 0 when every check passed, 1 otherwise.
    /// </summary>
    public class SmokeTestRunner : MonoBehaviour
    {
        string outputDirectory;
        HashSet<string> steps;
        string server;
        int expectedRemotes;
        readonly List<string> results = new List<string>();
        int failures;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Bootstrap()
        {
            string[] args = Environment.GetCommandLineArgs();
            string output = Argument(args, "-smoke-test");
            if (output == null)
                return;

            var go = new GameObject("Smoke Test Runner");
            DontDestroyOnLoad(go);
            var runner = go.AddComponent<SmokeTestRunner>();
            runner.outputDirectory = Path.GetFullPath(output);
            runner.steps = new HashSet<string>((Argument(args, "-smoke-only") ?? "menu,racing,rts").Split(','));
            runner.server = Argument(args, "-smoke-server");
            runner.expectedRemotes = int.TryParse(Argument(args, "-smoke-expect-remote"), out int remotes) ? remotes : 0;
        }

        static string Argument(string[] args, string name)
        {
            int index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        IEnumerator Start()
        {
            Directory.CreateDirectory(outputDirectory);
            GameSession.Autopilot = true;
            Application.runInBackground = true;
            Application.logMessageReceived += OnLog;
            Log($"Smoke test started: steps {string.Join(",", steps)}, resolution {Screen.width}x{Screen.height}");

            if (steps.Contains("menu"))
                yield return MenuStep();
            if (steps.Contains("racing"))
                yield return RacingStep();
            if (steps.Contains("rts"))
                yield return RtsStep();
            if (steps.Contains("online"))
                yield return OnlineStep();

            Log(failures == 0 ? "SMOKE TEST PASSED" : $"SMOKE TEST FAILED ({failures} failed checks)");
            File.WriteAllLines(Path.Combine(outputDirectory, "smoke-results.txt"), results);
            Application.Quit(failures == 0 ? 0 : 1);
        }

        IEnumerator MenuStep()
        {
            yield return LoadScene(Scenes.MainMenu);
            yield return new WaitForSecondsRealtime(1.5f);
            yield return Screenshot("01-main-menu");
            Check(FindAnyObjectByType<Menu.MainMenu>() != null, "Main menu loads");
        }

        IEnumerator RacingStep()
        {
            yield return LoadScene(Scenes.RacingSinglePlayer);
            CircuitManager circuit = FindAnyObjectByType<CircuitManager>();
            Check(circuit != null && circuit.WaypointCount > 0, "Racing scene loads with a circuit");
            if (circuit == null)
                yield break;

            yield return new WaitForSeconds(5f);
            yield return Screenshot("02-racing-start");
            for (int i = 0; i < 6; i++)
            {
                yield return new WaitForSeconds(2.5f);
                LogStandings(circuit);
            }
            yield return Screenshot("03-racing");

            IReadOnlyList<Competitor> standings = circuit.Standings;
            Check(standings.Count == 4, $"Four competitors race ({standings.Count} found)");
            foreach (Competitor competitor in standings)
                Check(competitor.Progress.WaypointsPassed >= 3,
                    $"{competitor.Nickname} drives through the waypoints ({competitor.Progress.WaypointsPassed} passed)");
        }

        void LogStandings(CircuitManager circuit)
        {
            var line = new StringBuilder("Standings:");
            foreach (Competitor c in circuit.Standings)
                line.Append($" {c.Position}. {c.Nickname} wp={c.Progress.WaypointsPassed} next={c.Progress.NextWaypoint} at {c.transform.position:F0};");
            Log(line.ToString());
        }

        IEnumerator RtsStep()
        {
            yield return LoadScene(Scenes.RtsBattlefield);
            ArmyController[] armies = FindObjectsByType<ArmyController>();
            Check(armies.Length == 2, "RTS scene has two armies");

            var seen = new HashSet<Unit>();
            var teamsWithUnits = new HashSet<int>();
            bool harvesting = false;
            float end = Time.time + 75f;
            bool screenshotTaken = false;
            while (Time.time < end)
            {
                foreach (Unit unit in FindObjectsByType<Unit>())
                {
                    seen.Add(unit);
                    teamsWithUnits.Add(unit.Team.TeamNumber);
                    if (unit is HarvesterUnit harvester && harvester.Harvest != HarvesterUnit.HarvestState.None)
                        harvesting = true;
                }
                if (!screenshotTaken && seen.Count >= 6)
                {
                    yield return new WaitForSeconds(3f);
                    yield return Screenshot("04-rts-units");
                    screenshotTaken = true;
                }
                if (seen.Count(u => u == null) >= 2)
                    break;
                yield return new WaitForSeconds(0.5f);
            }
            FrameUnits();
            yield return new WaitForSeconds(0.5f);
            yield return Screenshot("05-rts-battle");

            foreach (ArmyController army in armies)
                Check(teamsWithUnits.Contains(army.Team.TeamNumber), $"Army {army.Team.TeamNumber} trains units");
            Check(harvesting, "Harvesters go to work");
            Check(seen.Count(u => u == null) > 0, $"Units fight and die ({seen.Count(u => u == null)} of {seen.Count} died)");
        }

        /// <summary>Moves the RTS camera over the middle of the living units, where the fighting is.</summary>
        static void FrameUnits()
        {
            Camera camera = Camera.main;
            Unit[] alive = FindObjectsByType<Unit>().Where(u => u.IsAlive).ToArray();
            if (camera == null || alive.Length == 0)
                return;
            Vector3 centre = alive.Aggregate(Vector3.zero, (sum, u) => sum + u.transform.position) / alive.Length;
            // Keep the camera's height and pitch; shift it so it looks at the centre.
            float back = camera.transform.position.y / Mathf.Tan(camera.transform.eulerAngles.x * Mathf.Deg2Rad);
            camera.transform.position = new Vector3(centre.x, camera.transform.position.y, centre.z - back);
        }

        IEnumerator OnlineStep()
        {
            if (server == null || !server.Contains(":"))
            {
                Check(false, "Online step needs -smoke-server host:port");
                yield break;
            }
            string[] parts = server.Split(':');
            GameSession.ServerHost = parts[0];
            GameSession.ServerPort = int.Parse(parts[1]);
            GameSession.Nickname = "Smoke " + UnityEngine.Random.Range(100, 999);

            yield return LoadScene(Scenes.RacingOnline);
            OnlineRaceManager online = FindAnyObjectByType<OnlineRaceManager>();
            Check(online != null, "Online scene loads");
            if (online == null)
                yield break;

            float deadline = Time.time + 20f;
            while (Time.time < deadline && (!online.IsConnected || online.RemotePlayerCount < expectedRemotes))
                yield return null;
            Check(online.IsConnected, "Connects to the race server");
            Check(online.RemotePlayerCount >= expectedRemotes, $"Sees {expectedRemotes} other player(s) ({online.RemotePlayerCount})");

            yield return new WaitForSeconds(6f);
            yield return Screenshot("06-online-" + online.LocalPlayerId);
        }

        IEnumerator LoadScene(string sceneName)
        {
            AsyncOperation load = SceneManager.LoadSceneAsync(sceneName);
            while (!load.isDone)
                yield return null;
            yield return null;
        }

        IEnumerator Screenshot(string fileName)
        {
            yield return new WaitForEndOfFrame();
            Texture2D image = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Path.Combine(outputDirectory, fileName + ".png"), image.EncodeToPNG());
            Destroy(image);
            Log("Screenshot " + fileName);
        }

        void Check(bool passed, string description)
        {
            if (!passed)
                failures++;
            Log((passed ? "PASS  " : "FAIL  ") + description);
        }

        void OnLog(string condition, string stackTrace, LogType type)
        {
            // Any exception or error in the game counts as a failure.
            if (type == LogType.Exception || type == LogType.Error)
            {
                failures++;
                results.Add("ERROR " + condition);
            }
        }

        void Log(string line)
        {
            results.Add(line);
            Debug.Log("[Smoke] " + line);
        }
    }
}
