using Celeste;
using Monocle;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System;
using TAS.EverestInterop;
using Input = Celeste.Input;

namespace TAS {
	public class InputController {
		public List<InputRecord> inputs = new List<InputRecord>();
		private int currentFrame, inputIndex, frameToNext;
		public string filePath;
		private List<InputRecord> fastForwards = new List<InputRecord>();
		public Vector2? resetSpawn;
		public InputController(string filePath) {
			this.filePath = filePath;
		}

		public bool CanPlayback => inputIndex < inputs.Count;
		public bool HasFastForward => fastForwards.Count > 0;
		public int FastForwardSpeed => fastForwards.Count == 0 ? 1 : fastForwards[0].Frames == 0 ? 400 : fastForwards[0].Frames;
		public int CurrentFrame => currentFrame;
		public int CurrentInputFrame => currentFrame - frameToNext + Current.Frames;
		
		public InputRecord Current { get; set; }
		public InputRecord Previous {
			get {
				if (frameToNext != 0 && inputIndex - 1 >= 0 && inputs.Count > 0)
					return inputs[inputIndex - 1];
				return null;
			}
		}
		public InputRecord Next {
			get {
				if (frameToNext != 0 && inputIndex + 1 < inputs.Count)
					return inputs[inputIndex + 1];
				return null;
			}
		}

		public bool HasInput(Actions action) {
			InputRecord input = Current;
			return input.HasActions(action);
		}

		public bool HasInputPressed(Actions action) {
			InputRecord input = Current;
			return input.HasActions(action) && CurrentInputFrame == 1;
		}

		public bool HasInputReleased(Actions action) {
			InputRecord current = Current;
			InputRecord previous = Previous;
			return !current.HasActions(action) && previous != null && previous.HasActions(action) && CurrentInputFrame == 1;
		}

		public override string ToString() {
			if (frameToNext == 0 && Current != null) {
				return Current.ToString() + "(" + currentFrame.ToString() + ")";
			} else if (inputIndex < inputs.Count && Current != null) {
				int inputFrames = Current.Frames;
				int startFrame = frameToNext - inputFrames;
				return Current.ToString() + "(" + (currentFrame - startFrame).ToString() + " / " + inputFrames + " : " + currentFrame + ")";
			}
			return string.Empty;
		}

		public string NextInput() {
			if (frameToNext != 0 && inputIndex + 1 < inputs.Count)
				return inputs[inputIndex + 1].ToString();
			return string.Empty;
		}

		public void InitializePlayback() {
			int trycount = 5;
			while (!ReadFile() && trycount >= 0) {
				System.Threading.Thread.Sleep(50);
				trycount--;
			}

			currentFrame = 0;
			inputIndex = 0;
			if (inputs.Count > 0) {
				Current = inputs[0];
				frameToNext = Current.Frames;
			} else {
				Current = new InputRecord();
				frameToNext = 1;
			}
		}

		public void AdvanceFrame(bool reload) {
			if (reload) {
				//Reinitialize the file and simulate a replay of the TAS file up to the current point.
				int previousFrame = currentFrame - 1;
				InitializePlayback();
				currentFrame = Manager.IsLoading() ? previousFrame + 1 : previousFrame;

				while (currentFrame > frameToNext) {
					if (inputIndex + 1 >= inputs.Count) {
						inputIndex++;
						return;
					}
					if (Current.FastForward) {
						fastForwards.RemoveAt(0);
					}
					Current = inputs[++inputIndex];
					frameToNext += Current.Frames;
				}
				//prevents duplicating commands while Manager.IsLoading()
				if (Current.Command != null) {
					Current = inputs[++inputIndex];
				}
			}
			if (Manager.IsLoading())
				return;
			do {
				if (Current.Command != null) {
					Current.Command.Invoke();
				}
				if (inputIndex < inputs.Count) {
					if (currentFrame >= frameToNext) {
						if (inputIndex + 1 >= inputs.Count) {
							inputIndex++;
							return;
						}
						if (Current.FastForward) {
							fastForwards.RemoveAt(0);
						}
						Current = inputs[++inputIndex];
						frameToNext += Current.Frames;
					}
				}
			} while (Current.Command != null);
			currentFrame++;
			if (Manager.ExportSyncData)
				Manager.ExportPlayerInfo();
			Manager.SetInputs(Current);
		}

		public void InitializeRecording() {
			currentFrame = 0;
			inputIndex = 0;
			Current = new InputRecord();
			frameToNext = 0;
			inputs.Clear();
			fastForwards.Clear();
		}

		/*
		public void RecordPlayer() {
			InputRecord input = new InputRecord() { Line = inputIndex + 1, Frames = currentFrame };
			GetCurrentInputs(input);

			if (currentFrame == 0 && input == Current) {
				return;
			} else if (input != Current && !Manager.IsLoading()) {
				Current.Frames = currentFrame - Current.Frames;
				inputIndex++;
				if (Current.Frames != 0) {
					inputs.Add(Current);
				}
				Current = input;
			}
			currentFrame++;
		}
		*/

		private static void GetCurrentInputs(InputRecord record) {
			if (Input.Jump.Check || Input.MenuConfirm.Check) { record.Actions |= Actions.Jump; }
			if (Input.Dash.Check || Input.MenuCancel.Check || Input.Talk.Check) { record.Actions |= Actions.Dash; }
			if (Input.Grab.Check) { record.Actions |= Actions.Grab; }
			if (Input.MenuJournal.Check) { record.Actions |= Actions.Journal; }
			if (Input.Pause.Check) { record.Actions |= Actions.Start; }
			if (Input.QuickRestart.Check) { record.Actions |= Actions.Restart; }
			if (Input.MenuLeft.Check || Input.MoveX.Value < 0) { record.Actions |= Actions.Left; }
			if (Input.MenuRight.Check || Input.MoveX.Value > 0) { record.Actions |= Actions.Right; }
			if (Input.MenuUp.Check || Input.MoveY.Value < 0) { record.Actions |= Actions.Up; }
			if (Input.MenuDown.Check || Input.MoveY.Value > 0) { record.Actions |= Actions.Down; }
		}

		/*
		public void WriteInputs() {
			using (FileStream fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite)) {
				for (int i = 0; i < inputs.Count; i++) {
					InputRecord record = inputs[i];
					byte[] data = Encoding.ASCII.GetBytes(record.ToString() + "\r\n");
					fs.Write(data, 0, data.Length);
				}
				fs.Close();
			}
		}
		*/

		public bool ReadFile(string filePath = "Celeste.tas", int startLine = 0, int endLine = int.MaxValue, int studioLine = 0) {
			try {
				if (filePath == "Celeste.tas" && startLine == 0) {
					inputs.Clear();
					fastForwards.Clear();
					if (!File.Exists(filePath))
						return false;
				}
				int subLine = 0;
				using (StreamReader sr = new StreamReader(filePath)) {
					while (!sr.EndOfStream) {
						string line = sr.ReadLine();

						if (filePath == "Celeste.tas")
							studioLine++;
						subLine++;
						if (subLine < startLine)
							continue;
						if (subLine > endLine)
							break;

						if (InputCommands.TryExecuteCommand(this, line, studioLine))
							return true;

						InputRecord input = new InputRecord(studioLine, line);

						if (input.FastForward) {
							fastForwards.Add(input);

							if (inputs.Count > 0) {
								inputs[inputs.Count - 1].ForceBreak = input.ForceBreak;
								inputs[inputs.Count - 1].FastForward = true;
							}
						}
						else if (input.Frames != 0) {
							inputs.Add(input);
						}
					}
				}
				return true;
			} catch {
				return false;
			}
		}

	}
}
