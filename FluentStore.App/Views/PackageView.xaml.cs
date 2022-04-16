﻿using FluentStore.Helpers;
using FluentStore.Helpers.Continuity;
using FluentStore.Helpers.Continuity.Extensions;
using FluentStore.SDK;
using FluentStore.SDK.Helpers;
using FluentStore.SDK.Messages;
using FluentStore.Services;
using FluentStore.ViewModels;
using FluentStore.ViewModels.Messages;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Messaging;
using System;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using Windows.UI.Notifications;
using SplitButton = Microsoft.UI.Xaml.Controls.SplitButton;
using SplitButtonClickEventArgs = Microsoft.UI.Xaml.Controls.SplitButtonClickEventArgs;
using FluentStore.SDK.Users;
using FluentStore.SDK.Models;
using OwlCore.WinUI.AbstractUI.Controls;

namespace FluentStore.Views
{
    public sealed partial class PackageView : Page
    {
        public PackageView()
        {
            InitializeComponent();
            SetUpAnimations();

            ViewModel = new PackageViewModel();
        }

        FluentStoreAPI.FluentStoreAPI FSApi = Ioc.Default.GetRequiredService<FluentStoreAPI.FluentStoreAPI>();
        INavigationService NavigationService = Ioc.Default.GetRequiredService<INavigationService>();
        PackageService PackageService = Ioc.Default.GetRequiredService<PackageService>();

        public PackageViewModel ViewModel
        {
            get => (PackageViewModel)GetValue(ViewModelProperty);
            set => SetValue(ViewModelProperty, value);
        }
        public static readonly DependencyProperty ViewModelProperty =
            DependencyProperty.Register(nameof(ViewModel), typeof(PackageViewModel), typeof(PackageView), new PropertyMetadata(null));

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            object param = e.Parameter;

            if (param is PackageBase package)
            {
                ViewModel = new PackageViewModel(package);
            }
            else if (param is PackageViewModel vm)
            {
                ViewModel = vm;
            }
            else if (param is Garfoot.Utilities.FluentUrn.Urn urn)
            {
                WeakReferenceMessenger.Default.Send(new PageLoadingMessage(true));
                try
                {
                    ViewModel = new PackageViewModel(await PackageService.GetPackageAsync(urn));
                }
                catch (WebException ex)
                {
                    WeakReferenceMessenger.Default.Send(new PageLoadingMessage(false));
                    NavigationService.ShowHttpErrorPage(ex.StatusCode, ex.Message);
                }
                catch (Exception ex)
                {
                    WeakReferenceMessenger.Default.Send(new PageLoadingMessage(false));
                    NavigationService.ShowHttpErrorPage(418, ex.Message);
                }
                WeakReferenceMessenger.Default.Send(new PageLoadingMessage(false));
            }
            else if (param is Flurl.Url url)
            {
                WeakReferenceMessenger.Default.Send(new PageLoadingMessage(true));
                try
                {
                    ViewModel = new PackageViewModel(await PackageService.GetPackageFromUrlAsync(url));
                }
                catch (WebException ex)
                {
                    WeakReferenceMessenger.Default.Send(new PageLoadingMessage(false));
                    NavigationService.ShowHttpErrorPage(ex.StatusCode, ex.Message);
                }
                catch (Exception ex)
                {
                    WeakReferenceMessenger.Default.Send(new PageLoadingMessage(false));
                    NavigationService.ShowHttpErrorPage(418, ex.Message);
                }
                WeakReferenceMessenger.Default.Send(new PageLoadingMessage(false));
            }

            if (ViewModel?.Package != null)
            {
                WeakReferenceMessenger.Default.Send(new SetPageHeaderMessage("Apps"));

                bool canLaunch = false;
                try
                {
                    canLaunch = await ViewModel.Package.CanLaunchAsync();
                }
                catch (Exception ex)
                {
                    var logger = Ioc.Default.GetRequiredService<LoggerService>();
                    logger.UnhandledException(ex, "Exception from Win32 component");
                }
                if (canLaunch)
                    UpdateInstallButtonToLaunch();
            }
        }

        private async void AddToCollection_Click(object sender, RoutedEventArgs e)
        {
            FlyoutBase flyout;
            if (true)//!UserService.IsLoggedIn)
            {
                flyout = new Flyout
                {
                    Content = new TextBlock
                    {
                        Text = "Please create an account or\r\nlog in to access this feature.",
                        TextWrapping = TextWrapping.Wrap
                    },
                    Placement = FlyoutPlacementMode.Bottom
                };
            }
            else
            {
                try
                {
                    string userId = null;// UserService.CurrentUser.LocalID;
                    var collections = await FSApi.GetCollectionsAsync(userId);
                    if (collections.Count > 0)
                    {
                        flyout = new MenuFlyout
                        {
                            Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft
                        };
                        foreach (FluentStoreAPI.Models.Collection collection in collections)
                        {
                            if (ViewModel.Package is SDK.Packages.GenericPackageCollection<FluentStoreAPI.Models.Collection> curCollection
                                && curCollection.Model.Id == collection.Id)
                            {
                                // ABORT! Do not add to list of options. Attempting to view a collection that contains
                                // itself results in an infinite loop.
                                continue;
                            }

                            var item = new MenuFlyoutItem
                            {
                                Text = collection.Name,
                                Tag = collection
                            };
                            item.Click += (object s, RoutedEventArgs e) =>
                            {
                                var it = (MenuFlyoutItem)s;
                                var col = (FluentStoreAPI.Models.Collection)it.Tag;
                                col.Items ??= new System.Collections.Generic.List<string>(1);
                                col.Items.Add(ViewModel.Package.Urn.ToString());
                            };
                            ((MenuFlyout)flyout).Items.Add(item);
                        }
                        flyout.Closed += async (s, e) =>
                        {
                            foreach (var it in ((MenuFlyout)s).Items)
                            {
                                var col = (FluentStoreAPI.Models.Collection)it.Tag;
                                await FSApi.UpdateCollectionAsync(userId, col);
                            }
                        };
                    }
                    else
                    {
                        var myCollectionsLink = new Hyperlink
                        {
                            Inlines =
                            {
                                new Run { Text = "My Collections" }
                            },
                        };
                        myCollectionsLink.Click += (sender, args) =>
                        {
                            NavigationService.Navigate(typeof(MyCollectionsView));
                        };
                        var noCollectionsContent = new TextBlock
                        {
                            TextWrapping = TextWrapping.Wrap,
                            Inlines =
                            {
                                new Run { Text = "You don't have any collections." },
                                new LineBreak(),
                                new Run { Text = "Go to " },
                                myCollectionsLink,
                                new Run { Text = " to create one." }
                            }
                        };

                        flyout = new Flyout
                        {
                            Content = noCollectionsContent,
                            Placement = FlyoutPlacementMode.Bottom
                        };
                    }
                }
                catch (Flurl.Http.FlurlHttpException ex)
                {
                    flyout = new Controls.HttpErrorFlyout(ex.StatusCode ?? 418, ex.Message);
                }
                catch
                {
                    flyout = new Flyout
                    {
                        Content = new TextBlock
                        {
                            Text = "Please create an account or\r\nlog in to access this feature.",
                            TextWrapping = TextWrapping.Wrap
                        },
                        Placement = FlyoutPlacementMode.Bottom
                    };
                }
            }

            flyout.ShowAt((Button)sender);
        }

        private async void InstallSplitButton_Click(SplitButton sender, SplitButtonClickEventArgs e)
        {
            try
            {
                InstallButton.IsEnabled = false;
                RegisterPackageServiceMessages();
                VisualStateManager.GoToState(this, "Progress", true);

                if (ViewModel.Package.Status.IsLessThan(PackageStatus.Downloaded))
                    await ViewModel.Package.DownloadAsync();

                if (ViewModel.Package.Status.IsAtLeast(PackageStatus.Downloaded))
                {
                    bool installed = await ViewModel.Package.InstallAsync();
                    if (installed && await ViewModel.Package.CanLaunchAsync())
                        UpdateInstallButtonToLaunch();
                }
            }
            finally
            {
                InstallButton.IsEnabled = true;
                VisualStateManager.GoToState(this, "NoAction", true);
                WeakReferenceMessenger.Default.UnregisterAll(this);
            }
        }

        private async void Download_Click(object sender, RoutedEventArgs e)
        {
            InstallButton.IsEnabled = false;

            var progressToast = RegisterPackageServiceMessages();
            WeakReferenceMessenger.Default.Unregister<SuccessMessage>(this);
            WeakReferenceMessenger.Default.Register<SuccessMessage>(this, (r, m) =>
            {
                _ = DispatcherQueue.TryEnqueue(() => PackageHelper.HandlePackageDownloadCompletedToast(m, progressToast));
            });

            try
            {
                VisualStateManager.GoToState(this, "Progress", true);
                var downloadItem = await ViewModel.Package.DownloadAsync();

                if (downloadItem != null)
                {
                    PackageBase p = ViewModel.Package;
                    FileInfo file = (FileInfo)p.DownloadItem;
                    Windows.Storage.Pickers.FileSavePicker savePicker = new()
                    {
                        SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Downloads
                    };

                    // Initialize save picker for Win32
                    var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.Current.Window);
                    WinRT.Interop.InitializeWithWindow.Initialize(savePicker, hwnd);

                    savePicker.FileTypeChoices.Add(p.Type.GetExtensionDescription(), new string[] { file.Extension });
                    savePicker.SuggestedFileName = file.Name;

                    var userFile = await savePicker.PickSaveFileAsync();
                    if (userFile != null)
                    {
                        await Task.Run(() => file.MoveTo(userFile.Path, true));
                    }
                }
            }
            finally
            {
                InstallButton.IsEnabled = true;
                VisualStateManager.GoToState(this, "NoAction", true);
                WeakReferenceMessenger.Default.UnregisterAll(this);
            }
        }

        private void ShareButton_Click(SplitButton sender, SplitButtonClickEventArgs args)
        {
            DataTransferManager dataTransferManager = ShareHelper.GetDataTransferManager(App.Current.Window);
            dataTransferManager.DataRequested += (sender, args) =>
            {
                Flurl.Url appUrl = "fluentstore://package/" + ViewModel.Package.Urn.ToString();
                ShareDataRequested(sender, args, appUrl);
            };
            ShareHelper.ShowShareUIForWindow(App.Current.Window);
        }

        private void ShareWebLink_Click(object sender, RoutedEventArgs e)
        {
            DataTransferManager dataTransferManager = ShareHelper.GetDataTransferManager(App.Current.Window);
            dataTransferManager.DataRequested += (sender, args) =>
            {
                Flurl.Url appUrl = PackageService.GetUrlForPackageAsync(ViewModel.Package);
                ShareDataRequested(sender, args, appUrl);
            };
            ShareHelper.ShowShareUIForWindow(App.Current.Window);
        }

        private void OpenInBrowser_Click(object sender, RoutedEventArgs e)
        {
            Flurl.Url appUrl = PackageService.GetUrlForPackageAsync(ViewModel.Package);
            NavigationService.OpenInBrowser(appUrl);
        }

        private async void ShareDataRequested(DataTransferManager sender, DataRequestedEventArgs args, Flurl.Url appUrl)
        {
            var appUri = appUrl.ToUri();
            DataPackage linkPackage = new DataPackage();
            linkPackage.SetApplicationLink(appUri);

            DataRequest request = args.Request;
            request.Data.SetWebLink(appUri);
            request.Data.Properties.Title = "Share App";
            request.Data.Properties.Description = ViewModel.Package.ShortTitle;
            request.Data.Properties.ContentSourceApplicationLink = appUri;
            if (typeof(SDK.Images.StreamImage).IsAssignableFrom(ViewModel.AppIcon.GetType()))
            {
                var img = (SDK.Images.StreamImage)ViewModel.AppIcon;
                var imgStream = await img.GetImageStreamAsync();
                request.Data.Properties.Thumbnail = Windows.Storage.Streams.RandomAccessStreamReference.CreateFromStream(imgStream.AsRandomAccessStream());
            }
        }

        private void HeroImage_SizeChanged(object sender, RoutedEventArgs e)
        {
            UpdateHeroImageSpacer((FrameworkElement)sender);
        }

        private void InfoCard_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateHeroImageSpacer(HeroImage);
        }

        private void UpdateHeroImageSpacer(FrameworkElement imageElem)
        {
            // Height of the card including padding and spacing
            double cardHeight = InfoCard.ActualHeight + InfoCard.Margin.Top + InfoCard.Margin.Bottom
                + ((StackPanel)ContentScroller.Content).Spacing * 2;

            // Required amount of additional spacing to place the card at the bottom of the hero image,
            // or at the bottom of the page (whichever places the card higher up)
            double offset = Math.Min(imageElem.ActualHeight - cardHeight, ActualHeight - cardHeight);
            HeroImageSpacer.Height = Math.Max(offset, 0);
        }

        private void UpdateInstallButtonToLaunch()
        {
            InstallButtonText.Text = "Launch";
            InstallButton.Click -= InstallSplitButton_Click;
            InstallButton.Click += async (SplitButton sender, SplitButtonClickEventArgs e)
                => await ViewModel.Package.LaunchAsync();
        }

        public ToastNotification RegisterPackageServiceMessages()
        {
            var progressToast = PackageHelper.GenerateProgressToast(ViewModel.Package);

            WeakReferenceMessenger.Default.Register<ErrorMessage>(this, (r, m) =>
            {
                _ = DispatcherQueue.TryEnqueue(() =>
                {
                    if (m.Context is PackageBase p)
                    {
                        switch (m.Type)
                        {
                            case ErrorType.PackageDownloadFailed:
                                PackageHelper.HandlePackageDownloadFailedToast(m, progressToast);
                                break;

                            case ErrorType.PackageInstallFailed:
                                PackageHelper.HandlePackageInstallFailedToast(m, progressToast);
                                break;
                        }
                    }
                });
            });
            WeakReferenceMessenger.Default.Register<PackageFetchStartedMessage>(this, (r, m) =>
            {
                _ = DispatcherQueue.TryEnqueue(() =>
                {
                    ProgressIndicator.IsIndeterminate = true;
                    ProgressLabel.Text = "Fetching packages...";
                });
            });
            WeakReferenceMessenger.Default.Register<PackageDownloadStartedMessage>(this, (r, m) =>
            {
                _ = DispatcherQueue.TryEnqueue(() =>
                {
                    ProgressLabel.Text = "Downloading package...";

                    PackageHelper.HandlePackageDownloadStartedToast(m, progressToast);
                });
            });
            WeakReferenceMessenger.Default.Register<PackageDownloadProgressMessage>(this, (r, m) =>
            {
                _ = DispatcherQueue.TryEnqueue(() =>
                {
                    double prog = m.Downloaded / m.Total;
                    ProgressIndicator.IsIndeterminate = false;
                    ProgressIndicator.Value = prog;
                    ProgressText.Text = $"{prog * 100:##0}%";

                    PackageHelper.HandlePackageDownloadProgressToast(m, progressToast);
                });
            });
            WeakReferenceMessenger.Default.Register<PackageInstallStartedMessage>(this, (r, m) =>
            {
                _ = DispatcherQueue.TryEnqueue(() =>
                {
                    ProgressIndicator.IsIndeterminate = true;
                    ProgressText.Text = string.Empty;
                    ProgressLabel.Text = "Installing package...";

                    PackageHelper.HandlePackageInstallProgressToast(new(m.Package, 0), progressToast);
                });
            });
            WeakReferenceMessenger.Default.Register<PackageInstallProgressMessage>(this, (r, m) =>
            {
                _ = DispatcherQueue.TryEnqueue(() =>
                {
                    ProgressIndicator.IsIndeterminate = false;
                    ProgressIndicator.Value = m.Progress;
                    ProgressText.Text = $"{m.Progress * 100:##0}%";

                    PackageHelper.HandlePackageInstallProgressToast(m, progressToast);
                });
            });
            WeakReferenceMessenger.Default.Register<SuccessMessage>(this, (r, m) =>
            {
                _ = DispatcherQueue.TryEnqueue(() =>
                {
                    if (m.Context is PackageBase p)
                    {
                        switch (m.Type)
                        {
                            case SuccessType.PackageInstallCompleted:
                                PackageHelper.HandlePackageInstallCompletedToast(m, progressToast);
                                break;
                        }
                    }
                });
            });

            return progressToast;
        }

        private async void EditPackage_Click(object sender, RoutedEventArgs e)
        {
            // Check if package is editable
            if (ViewModel.Package is not SDK.Packages.IEditablePackage package)
                return;

            AbstractFormDialog editDialog = new(package.CreateEditForm(), Content.XamlRoot);

            if (await editDialog.ShowAsync() == ContentDialogResult.Primary)
            {
                try
                {
                    WeakReferenceMessenger.Default.Send(new PageLoadingMessage(true));

                    // User wants to save
                    await package.SaveAsync();
                    await ViewModel.Refresh();
                }
                catch (WebException ex)
                {
                    new Controls.HttpErrorFlyout(ex.StatusCode, ex.Message)
                        .ShowAt(InstallButton);
                }
                finally
                {
                    WeakReferenceMessenger.Default.Send(new PageLoadingMessage(false));
                }
            }
        }

        private void DeleteCollection_Click(object sender, RoutedEventArgs e)
        {
            FlyoutBase flyout;
            if (true)//!UserService.IsLoggedIn)
            {
                flyout = new Flyout
                {
                    Content = new TextBlock
                    {
                        Text = "Please create an account or\r\nlog in to access this feature.",
                        TextWrapping = TextWrapping.Wrap
                    },
                    Placement = FlyoutPlacementMode.Bottom
                };
            }
            else
            {
                var button = new Button
                {
                    Content = "Yes, delete forever",
                };
                flyout = new Flyout
                {
                    Content = new StackPanel
                    {
                        Spacing = 8,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = $"You are about to delete \"{ViewModel.Package.Title}\".\r\nDo you want to continue?",
                                TextWrapping = TextWrapping.Wrap
                            },
                            button
                        }
                    },
                    Placement = FlyoutPlacementMode.Bottom
                };
                button.Click += async (object sender, RoutedEventArgs e) =>
                {
                    string userId = null;// UserService.CurrentUser.LocalID;
                    // 0, urn; 1, namespace; 2, userId; 3, collectionId
                    string collectionId = ViewModel.Package.Urn.ToString().Split(':')[3];
                    try
                    {
                        if (await FSApi.DeleteCollectionAsync(userId, collectionId))
                            NavigationService.NavigateBack();
                    }
                    catch (Flurl.Http.FlurlHttpException ex)
                    {
                        // TODO: Show error message
                    }
                };
            }

            flyout.ShowAt((FrameworkElement)sender);
        }

        private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            string state = (App.Current.Window.Bounds.Width > (double)App.Current.Resources["CompactModeMinWidth"])
                ? "DefaultLayout" : "CompactLayout";
            VisualStateManager.GoToState(this, state, true);
        }

        private void SetUpAnimations()
        {
            var compositor = this.Visual().Compositor;

            // Create background visuals.
            //var infoCardVisual = compositor.CreateSpriteVisual();
            //var infoCardVisualBrush = infoCardVisual.Brush = compositor.CreateBackdropBrush();
            //InfoCard.SetChildVisual(infoCardVisual);

            // Sync background visual dimensions.
            //InfoCard.SizeChanged += (s, e) => infoCardVisual.Size = e.NewSize.ToVector2();

            // Enable implilcit Offset and Size animations.
            var easing = compositor.EaseOutSine();

            IconBox.EnableImplicitAnimation(VisualPropertyType.All, 400, easing: easing);
            TitleBlock.EnableImplicitAnimation(VisualPropertyType.All, 100, easing: easing);
            SubheadBlock.EnableImplicitAnimation(VisualPropertyType.All, 100, easing: easing);
            ActionBar.EnableImplicitAnimation(VisualPropertyType.All, 100, easing: easing);

            // Enable implicit Visible/Collapsed animations.
            ProgressGrid.EnableFluidVisibilityAnimation(axis: AnimationAxis.Y,
                showFromScale: Vector2.UnitX, hideToScale: Vector2.UnitX, showDuration: 400, hideDuration: 250);
        }

        private void Screenshot_Click(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is not SDK.Images.ImageBase img)
                return;

            // Show screenshot view
            ViewModel.SelectedScreenshot = img;
            FindName(nameof(ScreenshotView));
        }

        private void ScreenshotViewCloseButton_Click(object sender, RoutedEventArgs e)
        {
            UnloadObject(ScreenshotView);
        }
    }
}
