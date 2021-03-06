﻿using System;
using System.Linq;
using Mond.Debugger;
using IotWeb.Common.Http;

namespace Mond.RemoteDebugger
{
    internal class Session
    {
        private readonly SessionManager _manager;
        private readonly MondRemoteDebugger _debugger;
        private WebSocket _socket;

        public Session(SessionManager manager, MondRemoteDebugger debugger)
        {
            _manager = manager;
            _debugger = debugger;
        }

        public void OnOpen(WebSocket socket)
        {
            _socket = socket;
            _socket.DataReceived += OnMessage;

            _debugger.GetState(
                out var isRunning, out var programs, out var position, out var watches, out var callStack);

            var message = new MondValue(MondValueType.Object);
            message["Type"] = "InitialState";
            message["Programs"] = new MondValue(programs.Select(Utility.JsonProgram));
            message["Running"] = isRunning;
            message["Id"] = position.Id;
            message["StartLine"] = position.StartLine;
            message["StartColumn"] = position.StartColumn;
            message["EndLine"] = position.EndLine;
            message["EndColumn"] = position.EndColumn;
            message["Watches"] = new MondValue(watches.Select(Utility.JsonWatch));

            if (callStack != null)
                message["CallStack"] = _debugger.BuildCallStackArray(callStack);

            Send(Json.Serialize(message));
        }

        public void Send(string data) => _socket.Send(data);

        private void OnMessage(WebSocket sender, string data)
        {
            try
            {
                var obj = Json.Deserialize(data);

                switch ((string)obj["Type"])
                {
                    case "Action":
                        {
                            var value = (string)obj["Action"];

                            if (value == "break")
                            {
                                _debugger.RequestBreak();
                                break;
                            }

                            _debugger.PerformAction(ParseAction(value));
                            break;
                        }

                    case "SetBreakpoint":
                        {
                            var id = (int)obj["Id"];
                            var line = (int)obj["Line"];
                            var value = (bool)obj["Value"];

                            if (_debugger.SetBreakpoint(id, line, value))
                            {
                                var message = new MondValue(MondValueType.Object);
                                message["Type"] = "Breakpoint";
                                message["Id"] = id;
                                message["Line"] = line;
                                message["Value"] = value;

                                _manager.Broadcast(Json.Serialize(message));
                            }

                            break;
                        }

                    case "AddWatch":
                        {
                            var expression = (string)obj["Expression"];
                            _debugger.AddWatch(expression);
                            break;
                        }

                    case "RemoveWatch":
                        {
                            var id = (int)obj["Id"];
                            _debugger.RemoveWatch(id);
                            break;
                        }

                    default:
                        Console.WriteLine("unhandled message type: " + obj.Type);
                        break;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }

        private static MondDebugAction ParseAction(string value)
        {
            switch (value)
            {
                case "run":
                    return MondDebugAction.Run;

                case "step-in":
                    return MondDebugAction.StepInto;

                case "step-over":
                    return MondDebugAction.StepOver;

                case "step-out":
                    return MondDebugAction.StepOut;

                default:
                    throw new NotSupportedException("unknown action: " + value);
            }
        }
    }
}
