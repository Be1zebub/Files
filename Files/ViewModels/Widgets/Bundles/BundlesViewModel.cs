﻿using Files.Dialogs;
using Files.Enums;
using Files.EventArguments.Bundles;
using Files.Filesystem;
using Files.Helpers;
using Files.SettingsInterfaces;
using Files.ViewModels.Dialogs;
using Microsoft.Toolkit.Mvvm.ComponentModel;
using Microsoft.Toolkit.Mvvm.Input;
using Microsoft.Toolkit.Uwp;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;

namespace Files.ViewModels.Widgets.Bundles
{
    /// <summary>
    /// Bundles list View Model
    /// </summary>
    public class BundlesViewModel : ObservableObject, IDisposable
    {
        public bool noBundlesAddItemLoad = false;
        private string addBundleErrorText = string.Empty;
        private string bundleNameTextInput = string.Empty;
        private int internalCollectionCount;
        private bool isInitialized;
        private bool itemAddedInternally;

        public BundlesViewModel()
        {
            // Create commands
            InputTextKeyDownCommand = new RelayCommand<KeyRoutedEventArgs>(InputTextKeyDown);
            OpenAddBundleDialogCommand = new RelayCommand(OpenAddBundleDialog);
            AddBundleCommand = new RelayCommand(() => AddBundle(BundleNameTextInput));
            ImportBundlesCommand = new RelayCommand(ImportBundles);
            ExportBundlesCommand = new RelayCommand(ExportBundles);

            Items.CollectionChanged += Items_CollectionChanged;
        }

        public event EventHandler<BundlesLoadIconOverlayEventArgs> LoadIconOverlayEvent;

        public event EventHandler<BundlesOpenPathEventArgs> OpenPathEvent;

        public event EventHandler<string> OpenPathInNewPaneEvent;

        public ICommand AddBundleCommand { get; private set; }

        public string AddBundleErrorText
        {
            get => addBundleErrorText;
            set => SetProperty(ref addBundleErrorText, value);
        }

        public string BundleNameTextInput
        {
            get => bundleNameTextInput;
            set => SetProperty(ref bundleNameTextInput, value);
        }

        private IBundlesSettings BundlesSettings => App.BundlesSettings;
        public ICommand ExportBundlesCommand { get; private set; }

        public ICommand ImportBundlesCommand { get; private set; }

        public ICommand InputTextKeyDownCommand { get; private set; }

        /// <summary>
        /// Collection of all bundles
        /// </summary>
        public ObservableCollection<BundleContainerViewModel> Items { get; private set; } = new ObservableCollection<BundleContainerViewModel>();

        public bool NoBundlesAddItemLoad
        {
            get => noBundlesAddItemLoad;
            set => SetProperty(ref noBundlesAddItemLoad, value);
        }

        public ICommand OpenAddBundleDialogCommand { get; private set; }

        public (bool result, string reason) CanAddBundle(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                AddBundleErrorText = "BundlesWidgetAddBundleErrorInputEmpty".GetLocalized();
                return (false, "BundlesWidgetAddBundleErrorInputEmpty".GetLocalized());
            }

            if (!Items.Any((item) => item.BundleName == name))
            {
                AddBundleErrorText = string.Empty;
                return (true, string.Empty);
            }
            else
            {
                AddBundleErrorText = "BundlesWidgetAddBundleErrorAlreadyExists".GetLocalized();
                return (false, "BundlesWidgetAddBundleErrorAlreadyExists".GetLocalized());
            }
        }

        public void Dispose()
        {
            foreach (var item in Items)
            {
                item?.Dispose();
            }

            Items.CollectionChanged -= Items_CollectionChanged;
            Items = null;
        }

        public async Task Initialize()
        {
            await Load();
            isInitialized = true;
        }

        public async Task Load()
        {
            if (BundlesSettings.SavedBundles != null)
            {
                Items.Clear();

                // For every bundle in saved bundle collection:
                foreach (var bundle in BundlesSettings.SavedBundles)
                {
                    List<BundleItemViewModel> bundleItems = new List<BundleItemViewModel>();

                    // For every bundleItem in current bundle
                    foreach (var bundleItem in bundle.Value)
                    {
                        if (bundleItems.Count < Constants.Widgets.Bundles.MaxAmountOfItemsPerBundle)
                        {
                            if (bundleItem != null)
                            {
                                bundleItems.Add(new BundleItemViewModel(bundleItem, await StorageItemHelpers.GetTypeFromPath(bundleItem))
                                {
                                    ParentBundleName = bundle.Key,
                                    NotifyItemRemoved = NotifyBundleItemRemovedHandle,
                                    OpenPath = OpenPathHandle,
                                    OpenPathInNewPane = OpenPathInNewPaneHandle,
                                    LoadIconOverlay = LoadIconOverlayHandle
                                });
                                bundleItems.Last().UpdateIcon();
                            }
                        }
                    }

                    // Fill current bundle with collected bundle items
                    itemAddedInternally = true;
                    Items.Add(new BundleContainerViewModel()
                    {
                        BundleName = bundle.Key,
                        NotifyItemRemoved = NotifyItemRemovedHandle,
                        NotifyBundleItemRemoved = NotifyBundleItemRemovedHandle,
                        OpenPath = OpenPathHandle,
                        OpenPathInNewPane = OpenPathInNewPaneHandle,
                        LoadIconOverlay = LoadIconOverlayHandle
                    }.SetBundleItems(bundleItems));
                    itemAddedInternally = false;
                }

                if (Items.Count == 0)
                {
                    NoBundlesAddItemLoad = true;
                }
                else
                {
                    NoBundlesAddItemLoad = false;
                }
            }
            else // Null, therefore no items :)
            {
                NoBundlesAddItemLoad = true;
            }
        }

        public void Save()
        {
            if (BundlesSettings.SavedBundles != null)
            {
                Dictionary<string, List<string>> bundles = new Dictionary<string, List<string>>();

                // For every bundle in items bundle collection:
                foreach (var bundle in Items)
                {
                    List<string> bundleItems = new List<string>();

                    // For every bundleItem in current bundle
                    foreach (var bundleItem in bundle.Contents)
                    {
                        if (bundleItem != null)
                        {
                            bundleItems.Add(bundleItem.Path);
                        }
                    }

                    bundles.Add(bundle.BundleName, bundleItems);
                }

                BundlesSettings.SavedBundles = bundles; // Calls Set()
            }
        }

        private void AddBundle(string name)
        {
            if (!CanAddBundle(name).result)
            {
                return;
            }

            string savedBundleNameTextInput = name;
            BundleNameTextInput = string.Empty;

            if (BundlesSettings.SavedBundles == null || (BundlesSettings.SavedBundles?.ContainsKey(savedBundleNameTextInput) ?? false)) // Init
            {
                BundlesSettings.SavedBundles = new Dictionary<string, List<string>>()
                {
                    { savedBundleNameTextInput, new List<string>() { null } }
                };
            }

            itemAddedInternally = true;
            Items.Add(new BundleContainerViewModel()
            {
                BundleName = savedBundleNameTextInput,
                NotifyItemRemoved = NotifyItemRemovedHandle,
                NotifyBundleItemRemoved = NotifyBundleItemRemovedHandle,
                OpenPath = OpenPathHandle,
                OpenPathInNewPane = OpenPathInNewPaneHandle,
                LoadIconOverlay = LoadIconOverlayHandle
            });
            NoBundlesAddItemLoad = false;
            itemAddedInternally = false;

            // Save bundles
            Save();
        }

        private async void ExportBundles()
        {
            FileSavePicker filePicker = new FileSavePicker();
            filePicker.FileTypeChoices.Add("Json File", new List<string>() { System.IO.Path.GetExtension(Constants.LocalSettings.BundlesSettingsFileName) });

            StorageFile file = await filePicker.PickSaveFileAsync();

            if (file != null)
            {
                NativeFileOperationsHelper.WriteStringToFile(file.Path, (string)BundlesSettings.ExportSettings());
            }
        }

        private async void ImportBundles()
        {
            FileOpenPicker filePicker = new FileOpenPicker();
            filePicker.FileTypeFilter.Add(System.IO.Path.GetExtension(Constants.LocalSettings.BundlesSettingsFileName));

            StorageFile file = await filePicker.PickSingleFileAsync();

            if (file != null)
            {
                try
                {
                    string data = NativeFileOperationsHelper.ReadStringFromFile(file.Path);
                    var deserialized = JsonConvert.DeserializeObject<Dictionary<string, List<string>>>(data);
                    BundlesSettings.ImportSettings(JsonConvert.DeserializeObject<Dictionary<string, List<string>>>(data));
                    await Load(); // Update the collection
                }
                catch // Couldn't deserialize, data is corrupted
                {
                }
            }
        }

        private void InputTextKeyDown(KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Enter)
            {
                AddBundle(BundleNameTextInput);
                e.Handled = true;
            }
        }

        private void Items_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (internalCollectionCount < Items.Count && !itemAddedInternally)
            {
                Save();
            }

            internalCollectionCount = Items.Count;
        }

        private (byte[] IconData, byte[] OverlayData, bool IsCustom) LoadIconOverlayHandle(string path, uint thumbnailSize)
        {
            BundlesLoadIconOverlayEventArgs eventArgs = new BundlesLoadIconOverlayEventArgs(path, thumbnailSize);
            LoadIconOverlayEvent?.Invoke(this, eventArgs);

            return eventArgs.outData;
        }

        /// <summary>
        /// This function gets called when an item is removed to update the collection
        /// </summary>
        /// <param name="bundleContainer"></param>
        /// <param name="bundleItemPath"></param>
        private void NotifyBundleItemRemovedHandle(string bundleContainer, string bundleItemPath)
        {
            BundleItemViewModel itemToRemove = this.Items.Where((item) => item.BundleName == bundleContainer).First().Contents.Where((item) => item.Path == bundleItemPath).First();
            itemToRemove.RemoveItem();
        }

        /// <summary>
        /// This function gets called when an item is renamed to update the collection
        /// </summary>
        /// <param name="item"></param>
        private void NotifyBundleItemRemovedHandle(BundleItemViewModel item)
        {
            foreach (var bundle in Items)
            {
                if (bundle.BundleName == item.ParentBundleName)
                {
                    bundle.Contents.Remove(item);
                    item?.Dispose();

                    if (bundle.Contents.Count == 0)
                    {
                        bundle.NoBundleContentsTextVisibility = Visibility.Visible;
                    }
                }
            }
        }

        /// <summary>
        /// This function gets called when an item is removed to update the collection
        /// </summary>
        /// <param name="item"></param>
        private void NotifyItemRemovedHandle(BundleContainerViewModel item)
        {
            Items.Remove(item);
            item?.Dispose();

            if (Items.Count == 0)
            {
                NoBundlesAddItemLoad = true;
            }
        }

        private async void OpenAddBundleDialog()
        {
            TextBox inputText = new TextBox()
            {
                PlaceholderText = "BundlesWidgetAddBundleInputPlaceholderText".GetLocalized()
            };

            TextBlock tipText = new TextBlock()
            {
                Text = string.Empty,
                Visibility = Visibility.Collapsed
            };

            DynamicDialog dialog = new DynamicDialog(new DynamicDialogViewModel()
            {
                DisplayControl = new Grid()
                {
                    Children =
                    {
                        new StackPanel()
                        {
                            Spacing = 4d,
                            Children =
                            {
                                inputText,
                                tipText
                            }
                        }
                    }
                },
                TitleText = "BundlesWidgetCreateBundleDialogTitleText".GetLocalized(),
                SubtitleText = "BundlesWidgetCreateBundleDialogSubtitleText".GetLocalized(),
                PrimaryButtonText = "BundlesWidgetCreateBundleDialogPrimaryButtonText".GetLocalized(),
                CloseButtonText = "BundlesWidgetCreateBundleDialogCloseButtonText".GetLocalized(),
                PrimaryButtonAction = (vm, e) =>
                {
                    var (result, reason) = CanAddBundle(inputText.Text);

                    tipText.Text = reason;
                    tipText.Visibility = result ? Visibility.Collapsed : Visibility.Visible;

                    if (!result)
                    {
                        e.Cancel = true;
                        return;
                    }

                    AddBundle(inputText.Text);
                },
                CloseButtonAction = (vm, e) =>
                {
                    vm.HideDialog();
                },
                KeyDownAction = (vm, e) =>
                {
                    if (e.Key == VirtualKey.Enter)
                    {
                        AddBundle(inputText.Text);
                    }
                    else if (e.Key == VirtualKey.Escape)
                    {
                        vm.HideDialog();
                    }
                },
                DynamicButtons = DynamicDialogButtons.Primary | DynamicDialogButtons.Cancel
            });
            await dialog.ShowAsync();
        }

        private void OpenPathHandle(string path, FilesystemItemType itemType, bool openSilent, bool openViaApplicationPicker, IEnumerable<string> selectItems)
        {
            OpenPathEvent?.Invoke(this, new BundlesOpenPathEventArgs(path, itemType, openSilent, openViaApplicationPicker, selectItems));
        }

        private void OpenPathInNewPaneHandle(string path)
        {
            OpenPathInNewPaneEvent?.Invoke(this, path);
        }
    }
}