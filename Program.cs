﻿/*
    Copyright (c) Matthew Cash 2017, 
    All rights reserved.

    Redistribution and use in source and binary forms, with or without
    modification, are permitted provided that the following conditions are met:

    * Redistributions of source code must retain the above copyright notice, this
      list of conditions and the following disclaimer.

    * Redistributions in binary form must reproduce the above copyright notice,
      this list of conditions and the following disclaimer in the documentation
      and/or other materials provided with the distribution.

    THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
    AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
    IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
    DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
    FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
    DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
    SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
    CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
    OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
    OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
*/

using System;
using System.Diagnostics;

using CashCam.Files;
using CashLib.Module;
using CashLib.Localization;
using System.Reflection;
using CashLib;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Mono.Unix;

namespace CashCam
{
    class Program
    {


        //Handle for Windows machines


        private static CashLib.Threading.Thread SchedulerThread;
        private static CashLib.Threading.Thread CameraThread;
        private static CashLib.Tasks.Scheduler Scheduler;

        /// <summary>
        /// Determin if threads are running. Any threads should run when this is true.
        /// When set false all threads should cease execution. You should only set when you
        /// wish to kill the appliication.
        /// </summary>
        public static bool ThreadsRunning { get; set; }
        public static Action ThreadsStopped;
        public static Action ProgramEnding;

        /// <summary>
        /// Determin if the console is visisble.
        /// </summary>
        private static bool _consoleVisible;

        static void Main(string[] args)
        {
            _consoleVisible = true;

            foreach (string arg in args)
            {
                if (string.Compare(arg, "-d", true) == 0 ||
                    string.Compare(arg, "--daemon", true) == 0)
                    _consoleVisible = false;
                if(string.Compare(arg, "-h", true) == 0 ||
                    string.Compare(arg, "--help", true) == 0)
                {
                    PrintHelp();
                    return;
                }
            }

            ThreadsRunning = true;

            _initialize();

            ConsoleLoop();

            ProgramEnding?.Invoke();

            ThreadsStopped();
        }   

        private static void ConsoleLoop()
        {
            ConsoleKeyInfo input;
            ConsoleResponse response;
            List<string> commandHistory = new List<string>();
            int commandHistoryPosition = -1;
            bool newline = false; ;
            string line = "";
            while (ThreadsRunning)
            {
                if (_consoleVisible)
                {
                    commandHistoryPosition = -1;
                    line = "";
                    Console.Write("#>{0}", line);
                    do
                    {
                        input = Console.ReadKey(true);

                        if (input.Key == ConsoleKey.UpArrow)
                        {
                            if (commandHistory.Count == 0 || commandHistoryPosition == 0)
                                continue;
                            if (commandHistoryPosition == -1)
                            {
                                commandHistoryPosition = commandHistory.Count - 1;
                                commandHistory.Add(line);
                            }
                            else
                                commandHistoryPosition--;

                            line = commandHistory[commandHistoryPosition];
                            ClearWrite("#>{0}", line);
                        }

                        if (input.Key == ConsoleKey.DownArrow)
                        {
                            if (commandHistory.Count == 0 || commandHistoryPosition == -1) continue;

                            commandHistoryPosition++;

                            line = commandHistory[commandHistoryPosition];
                            ClearWrite("#>{0}", line);

                            if (commandHistoryPosition == commandHistory.Count - 1)
                            {
                                commandHistoryPosition = -1;
                                commandHistory.RemoveAt(commandHistory.Count - 1);
                            }
                        }

                        if (input.Key == ConsoleKey.Enter)
                        { newline = true; }
                        else if (input.Key == ConsoleKey.Backspace)
                        {
                            if (line.Length > 0)
                            {
                                line = line.Substring(0, line.Length - 1);
                                Console.CursorLeft--;
                                Console.Write(" ");
                                Console.CursorLeft--;
                            }

                        }
                        else
                        {
                            newline = false;
                            if (input.Key == ConsoleKey.Tab)
                            {
                                TabData td = TConsole.TabInput(line);
                                if (td.Result)
                                {
                                    Console.WriteLine();
                                    line = td.Line;
                                    if (td.TabStrings != null)
                                        Console.WriteLine(String.Join("\t", td.TabStrings));
                                    Console.Write("#>{0}", line);
                                }
                            }
                            else
                            {
                                if (input.KeyChar == '\0')
                                    continue;
                                line += input.KeyChar;
                                Console.Write(input.KeyChar);
                            }
                        }
                    } while (!newline);
                    Console.WriteLine();
                    
                    //Trim any blany lines from up/down arrows
                    if (commandHistory.Count != 0 && commandHistory[commandHistory.Count - 1] == "")
                        commandHistory.RemoveAt(commandHistory.Count - 1);

                    if (line.Trim() != "")
                    {
                        //Insert a new line since we ate the newline
                        commandHistory.Add(line);
                        response = TConsole.ProcessLine(line);
                        Console.WriteLine(response.Value);
                    }
                }
                else
                {
                    System.Threading.Thread.Sleep(1000);
                }
                System.Threading.Thread.Yield();
            }
        }

        private static void ClearWrite(string format, params string[] args)
        {
            int write = Console.CursorLeft;
            Console.CursorLeft = 0;
            for (int i = 0; i < write; i++)
                Console.Write(' ');
            Console.CursorLeft = 0;
            Console.Write(format, args);
        }

        public static void Stop()
        {
            ThreadsRunning = false;
        }

        static void PrintHelp()
        {
            Console.WriteLine(string.Format("CashCam Version {0}.{1}.{2}.{3}", Program.Version.Major, Program.Version.Minor, Program.Version.Build, Program.Version.Revision));
            Console.WriteLine();
            Console.WriteLine("Usage: {0} [OPTION]", System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName);
            Console.WriteLine();
            Console.WriteLine("Options");
            Console.WriteLine(" -h, --help\t\tPrint this help menu");
            Console.WriteLine(" -d, --daemon\t\tRun in daemon mode, no console input or output.");
            Console.WriteLine();
        }


        /// <summary>
        /// Function to initialize the application.
        /// </summary>
        private static void _initialize()
        {
            _processOSInit();

            Debug.Listeners.Clear();
            Trace.Listeners.Clear();

            TConsole.Init();


            Debug.Listeners.Add(new ConsoleTraceListener());
            Debug.Listeners.Add(new CashLib.Diagnostics.ConsoleTraceListiner());
            //Trace.Listeners.Add(new ConsoleTraceListener());
            //Trace.Listeners.Add(new TortoiseConsoleTraceListiner());

            Trace.WriteLine(string.Format("CashCam Version {0}.{1}.{2}.{3}", Program.Version.Major, Program.Version.Minor, Program.Version.Build, Program.Version.Revision));

            DefaultLanguage.InitDefault();
            ModuleInfo.LoadModules(Assembly.GetExecutingAssembly(), true);

            setupQuitFunction();

            Scheduler = new CashLib.Tasks.Scheduler("CashCam Scheduler");
            SchedulerThread = new CashLib.Threading.Thread("SchedulerThread");
            SchedulerThread.AddTask(Scheduler);

            CameraThread = new CashLib.Threading.Thread("CameraThread");
            CameraThread.AddTask(new Stream.StreamTask());

            //SchedulerThread.Start();
            CameraThread.Start();
            //ThreadsStopped += SchedulerThread.Stop;
            ThreadsStopped += CameraThread.Stop;
        }

        private static void setupQuitFunction()
        {
            TConsole.SetFunc("quit", new ConsoleFunction()
            {
                Function = quitConsoleFunction,
                HelpInfo = DefaultLanguage.Strings.GetString("quit_Help"),
            });

        }
        private static ConsoleResponse quitConsoleFunction(string[] arguments)
        {
            ThreadsRunning = false;
            return ConsoleResponse.NewSucess("Terminating.");
        }

        private static void _processOSInit()
        {
            int os = (int)Environment.OSVersion.Platform;
            if (os == 4 || os == 6 || os == 128)
                _processUnixInit();
            else
                _processWindowsInit();
        }

        #region "Windows Stuff"
        [DllImport("Kernel32")]
        private static extern bool SetConsoleCtrlHandler(EventHandler handler, bool add);

        private delegate bool EventHandler(CtrlType sig);

        enum CtrlType
        {
            CTRL_C_EVENT = 0,
            CTRL_BREAK_EVENT = 1,
            CTRL_CLOSE_EVENT = 2,
            CTRL_LOGOFF_EVENT = 5,
            CTRL_SHUTDOWN_EVENT = 6
        }

        private static bool Handler(CtrlType sig)
        {
            switch (sig)
            {
                case CtrlType.CTRL_C_EVENT:
                case CtrlType.CTRL_LOGOFF_EVENT:
                case CtrlType.CTRL_SHUTDOWN_EVENT:
                case CtrlType.CTRL_CLOSE_EVENT:
                    ThreadsRunning = false;
                    ProgramEnding?.Invoke();
                    return false;
                default:
                    return false;
            }
        }

        private static void _processWindowsInit()
        {
            SetConsoleCtrlHandler(Handler, true);
        }
        #endregion

        #region "Unix Stuff"
        private static System.Threading.Thread UnixSignalThread;


        private static void _processUnixInit()
        {
            UnixSignal[] signals = new UnixSignal[] {
                new UnixSignal(Mono.Unix.Native.Signum.SIGTERM),
                new UnixSignal(Mono.Unix.Native.Signum.SIGABRT),
                new UnixSignal(Mono.Unix.Native.Signum.SIGINT),
                new UnixSignal(Mono.Unix.Native.Signum.SIGUSR1)
            };
            
            UnixSignalThread = new System.Threading.Thread(() =>{
                int index = UnixSignal.WaitAny(signals, -1);
                ThreadsRunning = false;
            });           
        }
        #endregion


        /// <summary>
        /// Return the applicaion's version.
        /// </summary>
        public static Version Version
        {
            get
            {
                return typeof(Program).Assembly.GetName().Version;
            }
        }
    }
}