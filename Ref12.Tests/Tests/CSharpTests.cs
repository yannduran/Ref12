﻿using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.ComponentModel.Composition.Hosting;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using EnvDTE;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VSSDK.Tools.VsIdeTesting;
using SLaks.Ref12.Services;

namespace Ref12.Tests {
	[TestClass]
	public class IntegrationTests {
		public static DTE DTE { get { return VsIdeTestHostContext.Dte; } }

		// Hosted tests run out of a TestResults directory in the solution path.
		static readonly string SolutionDir = Path.Combine(Path.GetDirectoryName(typeof(IntegrationTests).Assembly.Location), @"..\..\..\Ref12.Tests\Fixtures\TestBed");

		[Export(typeof(IReferenceSourceProvider))]
		class RecordingSourceProvider : IReferenceSourceProvider {
			public RecordingSourceProvider() { AvailableAssemblies = new HashSet<string> { "mscorlib" }; }
			public ISet<string> AvailableAssemblies { get; private set; }

			public SymbolInfo LastSymbol { get; private set; }

			void IReferenceSourceProvider.Navigate(SymbolInfo symbol) {
				LastSymbol = symbol;
			}

			public void Reset() {
				LastSymbol = null;
			}
		}

		static readonly RecordingSourceProvider sourceRecord = new RecordingSourceProvider();
		private static IComponentModel componentModel;
		private static string fileName;
		private static ITextView textView;

		[ClassInitialize]
		public static void PrepareSolution(TestContext context) {
			DTE.Solution.Open(Path.Combine(SolutionDir, "TestBed.sln"));

			var part = AttributedModelServices.CreatePart(sourceRecord);
			componentModel = (IComponentModel)VsIdeTestHostContext.ServiceProvider.GetService(typeof(SComponentModel));
			((CompositionContainer)componentModel.DefaultCompositionService).Compose(new CompositionBatch(new[] { part }, null));

			fileName = Path.GetFullPath(Path.Combine(SolutionDir, "CSharp", "File.cs"));
			DTE.ItemOperations.OpenFile(fileName).Activate();
			textView = GetCurentTextView();
			System.Threading.Thread.Sleep(2000);	// Wait for the language service to bind the file; this can really take 2 seconds
		}

		[TestInitialize]
		public void ResetRecorder() {
			sourceRecord.Reset();
		}

		[TestMethod]
		[HostType("VS IDE")]
		public async Task CSharpGoToDefTest() {
			// Hop on to the UI thread so the language service APIs work
			await Application.Current.Dispatcher.NextFrame();

			textView.Caret.MoveTo(textView.FindSpan("Environment.GetFolderPath").End);
			GetCurrentNativeTextView().Execute(VSConstants.VSStd97CmdID.GotoDefn);
			Assert.IsFalse(sourceRecord.LastSymbol.HasLocalSource);
			Assert.AreEqual("mscorlib", sourceRecord.LastSymbol.AssemblyName);
			Assert.AreEqual("M:System.Environment.GetFolderPath(System.Environment.SpecialFolder)", sourceRecord.LastSymbol.IndexId);

			textView.Caret.MoveTo(textView.FindSpan("Environment.SpecialFolder.CommonOemLinks").End);
			GetCurrentNativeTextView().Execute(VSConstants.VSStd97CmdID.GotoDefn);
			Assert.IsFalse(sourceRecord.LastSymbol.HasLocalSource);
			Assert.AreEqual("mscorlib", sourceRecord.LastSymbol.AssemblyName);
			Assert.AreEqual("F:System.Environment.SpecialFolder.CommonOemLinks", sourceRecord.LastSymbol.IndexId);
		}

		[TestMethod]
		public async Task CSharp10ResolverTest() {
			await TestCSharpResolver(new CSharp10Resolver(DTE));
		}
		[TestMethod]
		public async Task CSharp12ResolverTest() {
			if (DTE.Version != "12.0")
				Assert.Inconclusive("CSharp12Resolver only works in VS 2013");

			await TestCSharpResolver(new CSharp10Resolver(DTE));
		}
		private async Task TestCSharpResolver(ISymbolResolver resolver) {
			// Hop on to the UI thread so the language service APIs work
			await Application.Current.Dispatcher.NextFrame();

			var symbol = resolver.GetSymbolAt(fileName, textView.FindSpan("\"\".Aggregate").End);
			Assert.IsFalse(symbol.HasLocalSource);
			Assert.AreEqual("System.Core", symbol.AssemblyName);
			Assert.AreEqual("M:System.Linq.Enumerable.Aggregate``2(System.Collections.Generic.IEnumerable{``0},``1,System.Func{``1,``0,``1})", symbol.IndexId);

			symbol = resolver.GetSymbolAt(fileName, textView.FindSpan("void M<").End - 1);
			Assert.IsTrue(symbol.HasLocalSource);
			Assert.AreEqual("M:CSharp.File.A`1.B`1.M``1(`0,`1,`0,``0)", symbol.IndexId);
		}

		///<summary>Gets the TextView for the active document.</summary>
		public static ITextView GetCurentTextView() {
			var editorAdapter = componentModel.GetService<IVsEditorAdaptersFactoryService>();

			return editorAdapter.GetWpfTextView(GetCurrentNativeTextView());
		}
		public static IVsTextView GetCurrentNativeTextView() {
			var textManager = (IVsTextManager)VsIdeTestHostContext.ServiceProvider.GetService(typeof(SVsTextManager));

			IVsTextView activeView = null;
			ErrorHandler.ThrowOnFailure(textManager.GetActiveView(1, null, out activeView));
			return activeView;
		}
	}
}
