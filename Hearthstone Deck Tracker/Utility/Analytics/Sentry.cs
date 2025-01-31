﻿#region

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BobsBuddy;
using BobsBuddy.Simulation;
using Hearthstone_Deck_Tracker.BobsBuddy;
using Hearthstone_Deck_Tracker.Enums;
using Hearthstone_Deck_Tracker.Hearthstone;
using Hearthstone_Deck_Tracker.Plugins;
using Hearthstone_Deck_Tracker.Utility.Extensions;
using Hearthstone_Deck_Tracker.Utility.Logging;
using SharpRaven;
using SharpRaven.Data;

#endregion


namespace Hearthstone_Deck_Tracker.Utility.Analytics
{
	internal class Sentry
	{
		static Sentry()
		{
			Client.Release = Helper.GetCurrentVersion().ToVersionString(true);
		}

		private static readonly RavenClient Client = new RavenClient("https://0a6c07cee8d141f0bee6916104a02af4:883b339db7b040158cdfc42287e6a791@app.getsentry.com/80405");

		public static string CaptureException(Exception ex)
		{
			var plugins = PluginManager.Instance.Plugins.Where(x => x.IsEnabled).ToList();
			ex.Data.Add("active-plugins", plugins.Any() ? string.Join(", ", plugins.Select(x => x.NameAndVersion)) : "none");

			var exception = new SentryEvent(ex);
#if(SQUIRREL)
			exception.Tags.Add("squirrel", "true");
#else
			exception.Tags.Add("squirrel", "false");
#endif
			exception.Tags.Add("hearthstone", Helper.GetHearthstoneBuild()?.ToString());
			if(!Helper.IsSigned)
				return "Executable was not properly signed.";
			return Client.Capture(exception);
		}

		private const int MaxBobsBuddyEvents = 10;
		private const int MaxBobsBuddyExceptions = 1;
		private static int BobsBuddyEventsSent;
		private static int BobsBuddyExceptionsSent;
		private static Queue<SentryEvent> BobsBuddyEvents = new Queue<SentryEvent>();

		public static void QueueBobsBuddyTerminalCase(Input testInput, Output output, string result, int turn, List<string> debugLog, Region region)
		{
			if(BobsBuddyEventsSent >= MaxBobsBuddyEvents)
				return;

			if(!Helper.IsSigned)
				return;

			// Clean up data
			output.ClearListsForReporting(); //ignoring for some temporary debugging

			var msg = new SentryMessage($"BobsBuddy {BobsBuddyUtils.VersionString}: Incorrect Terminal Case: {result}");

			var data = new BobsBuddyData()
			{
				ShortId = "",
				Turn = turn,
				Result = result,
				ThreadCount = BobsBuddyInvoker.ThreadCount,
				Iterations = output.simulationCount,
				ExitCondition = output.myExitCondition.ToString(),
				Input = testInput,
				Output = output,
				Log = ReverseAndClone(debugLog),
				Region = region,

			};

			var bbEvent = new SentryEvent(msg)
			{
				Level = ErrorLevel.Warning,
				Extra = data,
			};

			bbEvent.Tags.Add("region", data.Region.ToString());

			bbEvent.Fingerprint.Add(result);
			bbEvent.Fingerprint.Add(BobsBuddyUtils.VersionString);

			BobsBuddyEvents.Enqueue(bbEvent);
		}

		public static void SendQueuedBobsBuddyEvents(string? shortId)
		{
			while(BobsBuddyEvents.Count > 0)
			{
				if(BobsBuddyEventsSent >= MaxBobsBuddyEvents)
				{
					ClearBobsBuddyEvents();
					break;
				}
				var e = BobsBuddyEvents.Dequeue();
				((BobsBuddyData)e.Extra).ShortId = shortId;
				Client.Capture(e);
				BobsBuddyEventsSent++;
			}
		}

		public static void CaptureBobsBuddyException(Exception ex, Input? input, int turn, List<string> debugLog)
		{
			if(BobsBuddyExceptionsSent >= MaxBobsBuddyExceptions)
				return;
			if(input == null)
				return;
			BobsBuddyExceptionsSent++;

			// Clean up data
			var data = new BobsBuddyData()
			{
				ShortId = "",
				Turn = turn,
				ThreadCount = BobsBuddyInvoker.ThreadCount,
				Input = input,
				Log = ReverseAndClone(debugLog)
			};

			var bbEvent = new SentryEvent(ex)
			{
				Level = ErrorLevel.Warning,
				Extra = data,
			};

			bbEvent.Message = $"BobsBuddy {BobsBuddyUtils.VersionString}: {bbEvent.Message}";
			bbEvent.Fingerprint.Add(BobsBuddyUtils.VersionString);

			BobsBuddyEvents.Enqueue(bbEvent);
		}

		private static List<string> ReverseAndClone(List<string> toReverseAndClone)
		{
			var toReturn = toReverseAndClone.ToList();
			toReturn.Reverse();
			return toReturn;
		}

		public static void ClearBobsBuddyEvents() => BobsBuddyEvents.Clear();

		private class BobsBuddyData
		{
			public string? ShortId { get; set; }
			public int Turn { get; set; }
			public string? Result { get; set; }
			public int ThreadCount { get; set; }
			public int Iterations { get; set; }
			public string? ExitCondition { get; set; }
			public Input? Input { get; set; }
			public Output? Output { get; set; }

			public Region Region { get; set; }

			public List<string>? Log { get; set; }
			public string Replay => $"https://hsreplay.net/replay_debug/{ShortId}#turn={Turn}b";
		}
	}
}
