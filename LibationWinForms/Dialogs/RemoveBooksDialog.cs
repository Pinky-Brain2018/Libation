﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Linq;
using System.Windows.Forms;
using ApplicationServices;
using DataLayer;
using Dinah.Core.DataBinding;
using InternalUtilities;
using LibationWinForms.Login;

namespace LibationWinForms.Dialogs
{
	public partial class RemoveBooksDialog : Form
	{
		public bool BooksRemoved { get; private set; }

		private Account[] _accounts { get; }
		private readonly List<LibraryBook> _libraryBooks;
		private readonly SortableBindingList<RemovableGridEntry> _removableGridEntries;
		private readonly string _labelFormat;
		private int SelectedCount => SelectedEntries?.Count() ?? 0;
		private IEnumerable<RemovableGridEntry> SelectedEntries => _removableGridEntries?.Where(b => b.Remove);

		public RemoveBooksDialog(params Account[] accounts)
		{
			_libraryBooks = DbContexts.GetContext().GetLibrary_Flat_NoTracking();
			_accounts = accounts;

			InitializeComponent();
			_labelFormat = label1.Text;

			_dataGridView.CellContentClick += (s, e) => _dataGridView.CommitEdit(DataGridViewDataErrorContexts.Commit);
			_dataGridView.CellValueChanged += DataGridView1_CellValueChanged;
			_dataGridView.BindingContextChanged += (s, e) => UpdateSelection();

			var orderedGridEntries = _libraryBooks
				.Select(lb => new RemovableGridEntry(lb))
				.OrderByDescending(ge => (DateTime)ge.GetMemberValue(nameof(ge.PurchaseDate)))
				.ToList();

			_removableGridEntries = new SortableBindingList<RemovableGridEntry>(orderedGridEntries);
			gridEntryBindingSource.DataSource = _removableGridEntries;

			_dataGridView.Enabled = false;
		}

		private void DataGridView1_CellValueChanged(object sender, DataGridViewCellEventArgs e)
		{
			if (e.ColumnIndex == 0)
				UpdateSelection();
		}

		private async void RemoveBooksDialog_Shown(object sender, EventArgs e)
		{
			if (_accounts == null || _accounts.Length == 0)
				return;
			try
			{
				var rmovedBooks = await LibraryCommands.FindInactiveBooks((account) => new WinformResponder(account), _libraryBooks, _accounts);

				var removable = _removableGridEntries.Where(rge => rmovedBooks.Any(rb => rb.Book.AudibleProductId == rge.AudibleProductId));

				if (!removable.Any())
					return;

				foreach (var r in removable)
					r.Remove = true;

				UpdateSelection();
			}
			catch (Exception ex)
			{
				MessageBoxAlertAdmin.Show(
					"Error scanning library. You may still manually select books to remove from Libation's library.",
					"Error scanning library",
					ex);
			}
			finally
			{
				_dataGridView.Enabled = true;
			}
		}

		private void btnRemoveBooks_Click(object sender, EventArgs e)
		{
			var selectedBooks = SelectedEntries.ToList();

			if (selectedBooks.Count == 0) return;

			string titles = string.Join("\r\n", selectedBooks.Select(rge => "-" + rge.Title));

			string thisThese = selectedBooks.Count > 1 ? "these" : "this";
			string bookBooks = selectedBooks.Count > 1 ? "books" : "book";

			var result = MessageBox.Show(
				this,
				$"Are you sure you want to remove {thisThese} {selectedBooks.Count} {bookBooks} from Libation's library?\r\n\r\n{titles}",
				"Remove books from Libation?",
				MessageBoxButtons.YesNo,
				MessageBoxIcon.Question,
				MessageBoxDefaultButton.Button1);

			if (result == DialogResult.Yes)
			{
				using var context = DbContexts.GetContext();

				var libBooks = context.GetLibrary_Flat_NoTracking();

				var removeLibraryBooks = libBooks.Where(lb => selectedBooks.Any(rge => rge.AudibleProductId == lb.Book.AudibleProductId)).ToList();

				context.Library.RemoveRange(removeLibraryBooks);
				context.SaveChanges();

				foreach (var rEntry in selectedBooks)
					_removableGridEntries.Remove(rEntry);

				BooksRemoved = removeLibraryBooks.Count > 0;

				UpdateSelection();
			}
		}
		private void UpdateSelection()
		{
			_dataGridView.Sort(_dataGridView.Columns[0], ListSortDirection.Descending);
			var selectedCount = SelectedCount;
			label1.Text = string.Format(_labelFormat, selectedCount, selectedCount != 1 ? "s" : string.Empty);
			btnRemoveBooks.Enabled = selectedCount > 0;
		}
	}

	internal class RemovableGridEntry : GridEntry
	{
		private static readonly IComparer BoolComparer = new ObjectComparer<bool>();

		private bool _remove = false;
		public RemovableGridEntry(LibraryBook libraryBook) : base(libraryBook) { }

		public bool Remove
		{
			get
			{
				return _remove;
			}
			set
			{
				if (_remove != value)
				{
					_remove = value;
					NotifyPropertyChanged();
				}
			}
		}

		public override object GetMemberValue(string memberName)
		{
			if (memberName == nameof(Remove))
				return Remove;
			return base.GetMemberValue(memberName);
		}

		public override IComparer GetMemberComparer(Type memberType)
		{
			if (memberType == typeof(bool))
				return BoolComparer;
			return base.GetMemberComparer(memberType);
		}
	}
}
