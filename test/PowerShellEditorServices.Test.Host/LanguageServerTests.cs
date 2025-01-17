﻿//
// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
//

using Microsoft.PowerShell.EditorServices.Protocol.Client;
using Microsoft.PowerShell.EditorServices.Protocol.DebugAdapter;
using Microsoft.PowerShell.EditorServices.Protocol.LanguageServer;
using Microsoft.PowerShell.EditorServices.Protocol.MessageProtocol;
using Microsoft.PowerShell.EditorServices.Protocol.MessageProtocol.Channel;
using Microsoft.PowerShell.EditorServices.Protocol.Messages;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.PowerShell.EditorServices.Test.Host
{
    public class LanguageServerTests : ServerTestsBase, IAsyncLifetime
    {
        private LanguageServiceClient languageServiceClient;

        public Task InitializeAsync()
        {
            string testLogPath =
                Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "logs",
                    this.GetType().Name,
                    Guid.NewGuid().ToString().Substring(0, 8) + ".log");

            System.Console.WriteLine("        Output log at path: {0}", testLogPath);

            this.protocolClient =
            this.languageServiceClient =
                new LanguageServiceClient(
                    new StdioClientChannel(
                        "Microsoft.PowerShell.EditorServices.Host.exe",
                        "/logPath:\"" + testLogPath + "\"",
                        "/logLevel:Verbose"));

            return this.languageServiceClient.Start();
        }

        public Task DisposeAsync()
        {
            return this.languageServiceClient.Stop();
        }

        [Fact]
        public async Task ServiceReturnsSyntaxErrors()
        {
            // Send the 'didOpen' event
            await this.SendOpenFileEvent("TestFiles\\SimpleSyntaxError.ps1", false);

            // Wait for the diagnostic event
            PublishDiagnosticsNotification diagnostics = 
                await this.WaitForEvent(
                    PublishDiagnosticsNotification.Type);

            // Was there a syntax error?
            Assert.NotEqual(0, diagnostics.Diagnostics.Length);
            Assert.False(
                string.IsNullOrEmpty(diagnostics.Diagnostics[0].Message));
        }

        [Fact]
        public async Task ServiceCompletesFunctionName()
        {
            await this.SendOpenFileEvent("TestFiles\\CompleteFunctionName.ps1");

            CompletionItem[] completions =
                await this.SendRequest(
                    CompletionRequest.Type,
                    new TextDocumentPosition
                    {
                        Uri = "TestFiles\\CompleteFunctionName.ps1",
                        Position = new Position
                        {
                            Line = 4,
                            Character = 3,
                        }
                    });

            Assert.NotNull(completions);
            Assert.NotEqual(completions.Length, 0);

            // TODO: Add more asserts
        }

        [Fact]
        public async Task CompletesDetailOnVariableSuggestion()
        {
            await this.SendOpenFileEvent("TestFiles\\CompleteFunctionName.ps1");

            CompletionItem[] completions =
                await this.SendRequest(
                    CompletionRequest.Type,
                    new TextDocumentPosition
                    {
                        Uri = "TestFiles\\CompleteFunctionName.ps1",
                        Position = new Position
                        {
                            Line = 3,
                            Character = 5
                        }
                    });

            CompletionItem consoleFileNameItem =
                completions
                    .FirstOrDefault(
                        c => c.Label == "$ConsoleFileName");

            Assert.NotNull(consoleFileNameItem);
            Assert.Equal("[string]", consoleFileNameItem.Detail);
        }

        [Fact(Skip = "Skipped until variable documentation gathering is added back.")]
        public async Task CompletesDetailOnVariableDocSuggestion()
        {
            //await this.SendOpenFileEvent("TestFiles\\CompleteFunctionName.ps1");

            //await this.SendRequest(
            //    CompletionRequest.Type,
            //    new TextDocumentPosition
            //    {
            //        Uri = "TestFiles\\CompleteFunctionName.ps1",
            //        Position = new Position
            //        {
            //            Line = 7,
            //            Character = 5
            //        }
            //    });

            //CompletionsResponse completion = this.WaitForMessage<CompletionsResponse>();
            //List<string> entryName = new List<string>();
            //entryName.Add("$HKCU:");
            //await this.MessageWriter.WriteMessage(
            //    new CompletionDetailsRequest
            //    {
            //        Arguments = new CompletionDetailsRequestArgs
            //        {
            //            File = "TestFiles\\CompleteFunctionName.ps1",
            //            Line = 7,
            //            Offset = 5,
            //            EntryNames = entryName.ToArray()
            //        }
            //    });
            //CompletionDetailsResponse completionDetail = this.WaitForMessage<CompletionDetailsResponse>();
            //Assert.NotNull(completionDetail.Body[0]);
            //Assert.Equal("The software settings for the current user", completionDetail.Body[0].DocString);
        }

        [Fact]
        public async Task CompletesDetailOnCommandSuggestion()
        {
            await this.SendOpenFileEvent("TestFiles\\CompleteFunctionName.ps1");

            CompletionItem[] completions =
                await this.SendRequest(
                    CompletionRequest.Type,
                    new TextDocumentPosition
                    {
                        Uri = "TestFiles\\CompleteFunctionName.ps1",
                        Position = new Position
                        {
                            Line = 5,
                            Character = 8
                        }
                    });

            CompletionItem completionItem =
                completions
                    .FirstOrDefault(
                        c => c.Label == "Get-Process");

            Assert.NotNull(completionItem);

            CompletionItem updatedCompletionItem =
                await this.SendRequest(
                    CompletionResolveRequest.Type,
                    completionItem);

            // Can't depend on a particular documentation string if the test machine
            // hasn't run Update-Help, so just verify that a non-empty string was
            // returned.
            Assert.NotNull(updatedCompletionItem);
            Assert.True(updatedCompletionItem.Documentation.Length > 0);
        }

        [Fact]
        public async Task FindsReferencesOfVariable()
        {
            await this.SendOpenFileEvent("TestFiles\\FindReferences.ps1");

            Location[] locations =
            await this.SendRequest(
                ReferencesRequest.Type,
                new ReferencesParams
                {
                    Uri = "TestFiles\\FindReferences.ps1",
                    Position = new Position
                    {
                        Line = 7,
                        Character = 4,
                    }
                });

            Assert.NotNull(locations);
            Assert.Equal(locations.Length, 3);

            Assert.Equal(5, locations[0].Range.Start.Line);
            Assert.Equal(0, locations[0].Range.Start.Character);
            Assert.Equal(7, locations[1].Range.Start.Line);
            Assert.Equal(0, locations[1].Range.Start.Character);
            Assert.Equal(8, locations[2].Range.Start.Line);
            Assert.Equal(12, locations[2].Range.Start.Character);
        }

        [Fact]
        public async Task FindsNoReferencesOfEmptyLine()
        {
            await this.SendOpenFileEvent("TestFiles\\FindReferences.ps1");

            Location[] locations =
                await this.SendRequest(
                    ReferencesRequest.Type,
                    new ReferencesParams
                    {
                        Uri = "TestFiles\\FindReferences.ps1",
                        Position = new Position
                        {
                            Line = 9,
                            Character = 0,
                        }
                    });

            Assert.NotNull(locations);
            Assert.Equal(0, locations.Length);
        }

        [Fact]
        public async Task FindsReferencesOnFunctionDefinition()
        {
            await this.SendOpenFileEvent("TestFiles\\FindReferences.ps1");

            Location[] locations =
                await this.SendRequest(
                    ReferencesRequest.Type,
                    new ReferencesParams
                    {
                        Uri = "TestFiles\\FindReferences.ps1",
                        Position = new Position
                        {
                            Line = 0,
                            Character = 17,
                        }
                    });

            Assert.NotNull(locations);
            Assert.Equal(3, locations.Length);

            Assert.Equal(0, locations[0].Range.Start.Line);
            Assert.Equal(9, locations[0].Range.Start.Character);
            Assert.Equal(2, locations[1].Range.Start.Line);
            Assert.Equal(4, locations[1].Range.Start.Character);
            Assert.Equal(8, locations[2].Range.Start.Line);
            Assert.Equal(0, locations[2].Range.Start.Character);
        }

        [Fact]
        public async Task FindsReferencesOnCommand()
        {
            await this.SendOpenFileEvent("TestFiles\\FindReferences.ps1");

            Location[] locations =
                await this.SendRequest(
                    ReferencesRequest.Type,
                    new ReferencesParams
                    {
                        Uri = "TestFiles\\FindReferences.ps1",
                        Position = new Position
                        {
                            Line = 0,
                            Character = 17,
                        }
                    });

            Assert.NotNull(locations);
            Assert.Equal(3, locations.Length);

            Assert.Equal(0, locations[0].Range.Start.Line);
            Assert.Equal(9, locations[0].Range.Start.Character);
            Assert.Equal(2, locations[1].Range.Start.Line);
            Assert.Equal(4, locations[1].Range.Start.Character);
            Assert.Equal(8, locations[2].Range.Start.Line);
            Assert.Equal(0, locations[2].Range.Start.Character);
        }

        [Fact]
        public async Task FindsDefinitionOfCommand()
        {
            await this.SendOpenFileEvent("TestFiles\\FindReferences.ps1");

            Location[] locations =
                await this.SendRequest(
                    DefinitionRequest.Type,
                    new TextDocumentPosition
                    {
                        Uri = "TestFiles\\FindReferences.ps1",
                        Position = new Position
                        {
                            Line = 2,
                            Character = 11,
                        }
                    });

            Assert.NotNull(locations);
            Assert.Equal(1, locations.Length);
            Assert.Equal(0, locations[0].Range.Start.Line);
            Assert.Equal(9, locations[0].Range.Start.Character);
        }

        [Fact]
        public async Task FindsNoDefinitionOfBuiltinCommand()
        {
            await this.SendOpenFileEvent("TestFiles\\FindReferences.ps1");

            Location[] locations =
                await this.SendRequest(
                    DefinitionRequest.Type,
                    new TextDocumentPosition
                    {
                        Uri = "TestFiles\\FindReferences.ps1",
                        Position = new Position
                        {
                            Line = 10,
                            Character = 9,
                        }
                    });

            Assert.NotNull(locations);
            Assert.Equal(0, locations.Length);
        }

        [Fact]
        public async Task FindsDefintionOfVariable()
        {
            await this.SendOpenFileEvent("TestFiles\\FindReferences.ps1");

            Location[] locations =
                await this.SendRequest(
                    DefinitionRequest.Type,
                    new TextDocumentPosition
                    {
                        Uri = "TestFiles\\FindReferences.ps1",
                        Position = new Position
                        {
                            Line = 8,
                            Character = 13,
                        }
                    });

            Assert.NotNull(locations);
            Assert.Equal(1, locations.Length);
            Assert.Equal(5, locations[0].Range.Start.Line);
            Assert.Equal(0, locations[0].Range.Start.Character);
            Assert.Equal(5, locations[0].Range.End.Line);
            Assert.Equal(7, locations[0].Range.End.Character);
        }

        [Fact]
        public async Task FindsOccurencesOnFunctionDefinition()
        {
            await this.SendOpenFileEvent("TestFiles\\FindReferences.ps1");

            DocumentHighlight[] highlights =
                await this.SendRequest(
                    DocumentHighlightRequest.Type,
                    new TextDocumentPosition
                    {
                        Uri = "TestFiles\\FindReferences.ps1",
                        Position = new Position
                        {
                            Line = 0,
                            Character = 17,
                        }
                    });

            Assert.NotNull(highlights);
            Assert.Equal(3, highlights.Length);
            Assert.Equal(2, highlights[1].Range.Start.Line);
        }

        [Fact]
        public async Task GetsParameterHintsOnCommand()
        {
            await this.SendOpenFileEvent("TestFiles\\FindReferences.ps1");

            SignatureHelp signatureHelp =
                await this.SendRequest(
                    SignatureHelpRequest.Type,
                    new TextDocumentPosition
                    {
                        Uri = "TestFiles\\FindReferences.ps1",
                        Position = new Position
                        {
                            Line = 12,
                            Character = 14
                        }
                    });

            Assert.NotNull(signatureHelp);
            Assert.Equal(1, signatureHelp.Signatures.Length);
            Assert.Equal(2, signatureHelp.Signatures[0].Parameters.Length);
            Assert.Equal(
                "Write-Output [-InputObject] <psobject[]> [-NoEnumerate] [<CommonParameters>]",
                signatureHelp.Signatures[0].Label);
        }

        [Fact]
        public async Task ServiceExecutesReplCommandAndReceivesOutput()
        {
            OutputReader outputReader = new OutputReader(this.protocolClient);

            await 
                this.SendRequest(
                    EvaluateRequest.Type,
                    new EvaluateRequestArguments
                    {
                        Expression = "1 + 2"
                    });

            Assert.Equal("1 + 2", await outputReader.ReadLine());
            await outputReader.ReadLine(); // Skip the empty line
            Assert.Equal("3", await outputReader.ReadLine());
        }

        [Fact]
        public async Task ServiceExpandsAliases()
        {
            string expandedText =
                await this.SendRequest(
                    ExpandAliasRequest.Type,
                    "gci\r\npwd");

            Assert.Equal("Get-ChildItem\r\nGet-Location", expandedText);
        }

        [Fact]
        public async Task ServiceExecutesReplCommandAndReceivesChoicePrompt()
        {
            OutputReader outputReader = new OutputReader(this.protocolClient);

            string choiceScript =
                @"
                $caption = ""Test Choice"";
                $message = ""Make a selection"";
                $choiceA = New-Object System.Management.Automation.Host.ChoiceDescription ""&Apple"",""Help for Apple"";
                $choiceB = New-Object System.Management.Automation.Host.ChoiceDescription ""Banana"",""Help for Banana"";
                $choices = [System.Management.Automation.Host.ChoiceDescription[]]($choiceA,$choiceB);
                $host.ui.PromptForChoice($caption, $message, $choices, 1)";

            Task<Tuple<ShowChoicePromptRequest, RequestContext<ShowChoicePromptResponse>>> choicePromptTask =
                this.WaitForRequest(ShowChoicePromptRequest.Type);

            // Execute the script but don't await the task yet because
            // the choice prompt will block execution from completing
            Task<EvaluateResponseBody> evaluateTask =
                this.SendRequest(
                    EvaluateRequest.Type,
                    new EvaluateRequestArguments
                    {
                        Expression = choiceScript,
                        Context = "repl"
                    });

            // Wait for the choice prompt request and check expected values
            Tuple<ShowChoicePromptRequest, RequestContext<ShowChoicePromptResponse>> requestResponseContext = await choicePromptTask;
            ShowChoicePromptRequest showChoicePromptRequest = requestResponseContext.Item1;
            RequestContext<ShowChoicePromptResponse> requestContext = requestResponseContext.Item2;

            Assert.Equal(1, showChoicePromptRequest.DefaultChoice);

            // Respond to the prompt request
            await requestContext.SendResult(
                new ShowChoicePromptResponse
                {
                    ChosenItem = "a"
                });

            // Skip the initial script lines (6 script lines plus 3 blank lines)
            await outputReader.ReadLines(9);

            // Wait for the selection to appear as output
            await evaluateTask;
            Assert.Equal("0", await outputReader.ReadLine());
        }

        [Fact]
        public async Task ServiceExecutesReplCommandAndReceivesInputPrompt()
        {
            OutputReader outputReader = new OutputReader(this.protocolClient);

            string promptScript =
                @"
                $NameField = New-Object System.Management.Automation.Host.FieldDescription ""Name""
                $NameField.SetParameterType([System.String])
                $fields = [System.Management.Automation.Host.FieldDescription[]]($NameField)
                $host.ui.Prompt($null, $null, $fields)";

            Task<Tuple<ShowInputPromptRequest, RequestContext<ShowInputPromptResponse>>> inputPromptTask =
                this.WaitForRequest(ShowInputPromptRequest.Type);

            // Execute the script but don't await the task yet because
            // the choice prompt will block execution from completing
            Task<EvaluateResponseBody> evaluateTask =
                this.SendRequest(
                    EvaluateRequest.Type,
                    new EvaluateRequestArguments
                    {
                        Expression = promptScript,
                        Context = "repl"
                    });
            
            // Wait for the input prompt request and check expected values
            Tuple<ShowInputPromptRequest, RequestContext<ShowInputPromptResponse>> requestResponseContext = await inputPromptTask;
            ShowInputPromptRequest showInputPromptRequest = requestResponseContext.Item1;
            RequestContext<ShowInputPromptResponse> requestContext = requestResponseContext.Item2;

            Assert.Equal("Name", showInputPromptRequest.Name);

            // Respond to the prompt request
            await requestContext.SendResult(
                new ShowInputPromptResponse
                {
                    ResponseText = "John"
                });

            // Skip the initial script lines (4 script lines plus 2 blank lines)
            string[] scriptLines = await outputReader.ReadLines(6);

            // In some cases an extra newline appears after the script lines.
            // I have no idea why this happens, but it normally seems to occur
            // on my local machine and not the CI server.  For now, adjust for
            // it here.
            string outputLine = await outputReader.ReadLine();
            if (string.IsNullOrEmpty(outputLine))
            {
                outputLine = await outputReader.ReadLine();
            }

            // Verify the first line
            Assert.Equal("Name: John", outputLine);

            // Verify the rest of the output
            string[] outputLines = await outputReader.ReadLines(4);
            Assert.Equal("", outputLines[0]);
            Assert.Equal("Key  Value", outputLines[1]);
            Assert.Equal("---  -----", outputLines[2]);
            Assert.Equal("Name John ", outputLines[3]);

            // Wait for execution to complete
            await evaluateTask;
        }

        private async Task SendOpenFileEvent(string filePath, bool waitForDiagnostics = true)
        {
            string fileContents = string.Join(Environment.NewLine, File.ReadAllLines(filePath));

            // Start the event waiter for diagnostics before sending the
            // open event to make sure that we catch it
            Task<PublishDiagnosticsNotification> diagnosticWaitTask = null;
            if (waitForDiagnostics)
            {
                // Wait for the diagnostic event
                diagnosticWaitTask = 
                    this.WaitForEvent(
                        PublishDiagnosticsNotification.Type);
            }

            await this.SendEvent(
                DidOpenTextDocumentNotification.Type,
                new DidOpenTextDocumentNotification()
                {
                    Uri = filePath,
                    Text = fileContents
                });

            if (diagnosticWaitTask != null)
            {
                await diagnosticWaitTask;
            }
        }
    }
}
