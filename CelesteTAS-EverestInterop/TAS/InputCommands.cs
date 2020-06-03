﻿using System;
using System.IO;
using System.Reflection;

namespace TAS {
	public class InputCommands {
		/* Additional commands can be added by giving them the TASCommand attribute and naming them (CommandName)Command.
		 * The execute at start field indicates whether a command should be executed while building the input list (read, play)
		 * or when playing the file (console).
		 * The args field should list formats the command takes. This is not currently used but may be implemented into Studio
		 * in the future.
		 * Commands that execute at start must be void Command(InputController, string[], int).
		 * Commands that execute during playback must be void Command(string[])
		 */
		public static string[] Split(string line) {
			if (line.Contains(","))
				return line.Trim().Split(',');
			return line.Trim().Split();
		}

		public static string[] TrimArray(string[] array, int toTrim) {
			string[] output = new string[array.Length - toTrim];
			for (int i = toTrim; i < array.Length; i++) {
				output[i - toTrim] = array[i];
			}
			return output;
		}

		public static bool TryExecuteCommand(InputController state, string line, int studioLine) {
			try {
				if (char.IsLetter(line[0])) {
					string[] args = Split(line);
					string commandType = args[0] + "Command";
					MethodInfo method = typeof(InputCommands).GetMethod(commandType, BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.IgnoreCase);
					if (method == null)
						return false;

					string[] commandArgs = TrimArray(args, 1);
					TASCommandAttribute attribute = (TASCommandAttribute)method.GetCustomAttribute(typeof(TASCommandAttribute));
					if (!(Manager.enforceLegal && attribute.illegalInMaingame)) {
						if (attribute.executeAtStart) {
							method.Invoke(null, new object[] { state, commandArgs, studioLine });
							//the play command needs to stop reading the current file when it's done to prevent recursion
							return commandType.ToLower() == "playcommand";
						}

						Action commandCall = () => method.Invoke(null, new object[] { commandArgs });
						state.inputs.Add(new InputRecord(commandCall));
					}
				}
				return false;
			}
			catch { return false; }
		}


		[TASCommand(executeAtStart = true, args = new string[] {
			"Read,Path",
			"Read,Path,StartLine",
			"Read,Path,StartLine,EndLine"
		})]
		private static void ReadCommand(InputController state, string[] args, int studioLine) {
			string filePath = args[0];
			string origFilePath = Manager.settings.DefaultPath;
			// Check for full and shortened Read versions for absolute path
			if (origFilePath != null) {
				string altFilePath = origFilePath + Path.DirectorySeparatorChar + filePath;
				if (File.Exists(altFilePath))
					filePath = altFilePath;
				else {
					string[] files = Directory.GetFiles(origFilePath, $"{filePath}*.tas");
					if (files.Length != 0)
						filePath = files[0].ToString();
				}
			}
			// Check for full and shortened Read versions for relative path
			if (!File.Exists(filePath)) {
				string[] files = Directory.GetFiles(Directory.GetCurrentDirectory(), $"{filePath}*.tas");
				filePath = files[0].ToString();
				if (!File.Exists(filePath)) { return; }
			}
			// Find starting and ending lines
			int skipLines = 0;
			int lineLen = int.MaxValue;
			if (args.Length > 1) {
				string startLine = args[1];
				GetLine(startLine, filePath, out skipLines);
				if (args.Length > 2) {
					string endLine = args[2];
					GetLine(endLine, filePath, out lineLen);
				}
			}
			state.ReadFile(filePath, skipLines, lineLen, studioLine);
		}

		[TASCommand(illegalInMaingame = true, args = new string[] {
			"Console CommandType",
			"Console CommandType CommandArgs",
			"Console LoadCommand IDorSID",
			"Console LoadCommand IDorSID Screen",
			"Console LoadCommand IDorSID Screen Checkpoint",
			"Console LoadCommand IDorSID X Y"
		})]
		private static void ConsoleCommand(string[] args) {
			ConsoleHandler.ExecuteCommand(args);
		}

		[TASCommand(executeAtStart = true, args = new string[] {
			"Play,StartLine",
			"Play,StartLine,FramesToWait"
		})]
		private static void PlayCommand(InputController state, string[] args, int studioLine) {
			GetLine(args[0], state.filePath, out int startLine);
			if (args.Length > 1 && int.TryParse(args[1], out _))
				state.inputs.Add(new InputRecord(studioLine, args[1]));
			state.ReadFile(state.filePath, startLine);
		}

		[TASCommand(args = new string[] {
			"StartExport",
			"StartExport,Path",
			"StartExport,EntitiesToTrack",
			"StartExport,Path,EntitiesToTrack"
		})]
		private static void StartExportCommand(string[] args) {
			string path = "dump.txt";
			if (args.Length > 0) {
				if (args[0].Contains(".")) {
					path = args[0];
					args = TrimArray(args, 1);
				}
			}
			Manager.BeginExport(path, args);
			Manager.ExportSyncData = true;
		}

		[TASCommand(args = new string[] { "FinishExport" })]
		private static void FinishExportCommand(string[] args) {
			Manager.EndExport();
			Manager.ExportSyncData = false;
		}

		[TASCommand(args = new string[] { "EnforceLegal" })]
		private static void EnforceLegalCommand(string[] args) {
			Manager.enforceLegal = true;
		}

		[TASCommand(executeAtStart = true, args = new string[] { "Unsafe" })]
		private static void UnsafeCommand(InputController state, string[] args, int studioLine) {
			Manager.allowUnsafeInput = true;
		}

		private static void GetLine(string labelOrLineNumber, string path, out int lineNumber) {
			if (!int.TryParse(labelOrLineNumber, out lineNumber)) {
				int curLine = 0;
				using (StreamReader sr = new StreamReader(path)) {
					while (!sr.EndOfStream) {
						curLine++;
						string line = sr.ReadLine();
						if (line == ("#" + labelOrLineNumber)) {
							lineNumber = curLine;
							return;
						}
					}
					lineNumber = int.MaxValue;
				}
			}
		}
	}
}