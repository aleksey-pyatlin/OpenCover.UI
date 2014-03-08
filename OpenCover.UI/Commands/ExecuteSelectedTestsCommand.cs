﻿//
// This source code is released under the MIT License;
//
using Microsoft.VisualStudio.Shell.Interop;
using OpenCover.UI.Helpers;
using OpenCover.UI.Model.Test;
using OpenCover.UI.Processors;
using OpenCover.UI.Views;
using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace OpenCover.UI.Commands
{
	/// <summary>
	/// Executes the selected tests
	/// </summary>
	public class ExecuteSelectedTestsCommand : Command
	{
		private OpenCoverUIPackage _package;
		private IVsUIShell _uiShell;
		private IEnumerable<TestMethod> _selectedTests;
		private TestExplorerControl _testExplorerControl;

		public bool IsRunningCodeCoverage
		{
			get;
			private set;
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="ExecuteSelectedTestsCommand"/> class.
		/// </summary>
		/// <param name="package">The Visual Studio Extension Package.</param>
		public ExecuteSelectedTestsCommand(OpenCoverUIPackage package, IVsUIShell uiShell)
			: base(package, new CommandID(GuidList.GuidOpenCoverTestExplorerContextMenuCommandSet, (int)PkgCmdIDList.CommandIDOpenCoverTestExplorerRunTestWithOpenCover))
		{
			_package = package;
			_uiShell = uiShell;

			// FetchTestsTreeView();

			base.Enabled = false;

			_testExplorerControl = _package.ToolWindows.OfType<TestExplorerToolWindow>().First().TestExplorerControl;
			_testExplorerControl.TestDiscoveryFinished += OnTestDiscoveryFinished;
		}

		/// <summary>
		/// Event handler for TestExplorerControl.TestDiscoveryFinished.
		/// </summary>
		void OnTestDiscoveryFinished()
		{
			var hasTests = _testExplorerControl.TestsTreeView.Root != null && _testExplorerControl.TestsTreeView.Root.Children.Any();
			if (hasTests)
			{
				Enabled = true;
			}
			else
			{
				Enabled = false;
			}
		}

		/// <summary>
		/// Called when the command is executed.
		/// </summary>
		protected override void OnExecute()
		{
			var testGroupCollection = _testExplorerControl.TestsTreeView.Root;
			var testsItemSource = (testGroupCollection as TestClassContainer).Classes;

			// Need to select all tests which are under the selected group.
			var testsInSelectedGroup = testGroupCollection.Children.Where(tg => tg.IsSelected).SelectMany(tg =>
										{
											var testClass = tg as TestClass;
											return testsItemSource.Where(tc => tc == testClass).SelectMany(tc => tc.TestMethods);
										});

			// Need to select only those tests which are selected under not selected groups.
			var testsInNotSelectedGroup = testGroupCollection.Children.Where(tg => !tg.IsSelected).SelectMany(tg => tg.Children.Where(test => test.IsSelected)).Cast<TestMethod>();

			// Union of both tests is our selected tests
			_selectedTests = testsInNotSelectedGroup.Union(testsInSelectedGroup);

			if (_selectedTests.Any())
			{
				// show tool window which shows the progress.
				ShowCodeCoverageResultsToolWindow();

				Enabled = false;

				MessageBox.Show("Please wait while we collect code coverage results. The results will be shown in 'Code Coverage Results' window!", "Code Coverage", MessageBoxButton.OK, MessageBoxImage.Information);

				_package.VSEventsHandler.BuildDone += RunOpenCover;
				_package.VSEventsHandler.BuildSolution();
			}
			else
			{
				MessageBox.Show("Please select a test to run", "Code Coverage", MessageBoxButton.OK, MessageBoxImage.Error);
			}
		}

		/// <summary>
		/// Runs OpenCover for gathering code coverage details. This method gets called after the build is completed
		/// </summary>
		private void RunOpenCover()
		{
			// TODO: Check validity of tests
			Task.Factory.StartNew(
				() =>
				{
					var testExecutor = new TestExecutor(_package, _selectedTests);
					Tuple<string, string> files = testExecutor.Execute();
					var finalResults = testExecutor.GetExecutionResults();

					_package.ToolWindows.OfType<CodeCoverageResultsToolWindow>().First().CodeCoverageResultsControl.UpdateCoverageResults(finalResults);

					// if the tool window is hidden, show it again.
					ShowCodeCoverageResultsToolWindow();

					Enabled = true;
				});

			_package.VSEventsHandler.BuildDone -= RunOpenCover;
		}

		private void ShowCodeCoverageResultsToolWindow()
		{
			_package.Commands.OfType<CodeCoverageToolWindowCommand>().First().Invoke();
		}
	}
}