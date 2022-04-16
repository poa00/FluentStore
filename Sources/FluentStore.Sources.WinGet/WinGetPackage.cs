﻿using FluentStore.SDK.Helpers;
using FluentStore.SDK.Images;
using FluentStore.SDK.Messages;
using Garfoot.Utilities.FluentUrn;
using CommunityToolkit.Diagnostics;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Messaging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WinGetRun;
using WinGetRun.Models;
using System.IO;
using FluentStore.SDK;
using FluentStore.SDK.Models;

namespace FluentStore.Sources.WinGet
{
    public class WinGetPackage : PackageBase<Package>
    {
        private readonly WinGetApi WinGetApi = Ioc.Default.GetService<WinGetApi>();

        public WinGetPackage(PackageHandlerBase packageHandler, Package pack = null)
            : base(packageHandler)
        {
            if (pack != null)
                Update(pack);
        }

        public void Update(Package pack)
        {
            Guard.IsNotNull(pack, nameof(pack));
            Model = pack;

            // Set base properties
            Urn = Urn.Parse($"urn:{WinGetHandler.NAMESPACE_WINGET}:{Model.Id}");
            Title = pack.Latest.Name;
            PublisherId = pack.GetPublisherAndPackageIds().PublisherId;
            DeveloperName = pack.Latest.Publisher;
            ReleaseDate = pack.CreatedAt;
            Description = pack.Latest.Description;
            Version = pack.Versions[^1];
            Website = Link.Create(pack.Latest.Homepage, ShortTitle + " website");

            // Set WinGet package properties
            PackageId = pack.GetPublisherAndPackageIds().PackageId;
        }

        public void Update(Manifest manifest)
        {
            Guard.IsNotNull(manifest, nameof(manifest));
            Manifest = manifest;
            var installer = Manifest.Installers[0];

            PackageUri = new Uri(installer.Url);
            Website = Link.Create(Manifest.Homepage, ShortTitle + " website");

            if (installer.InstallerType.HasValue)
            {
                Type = installer.InstallerType.Value.ToSDKInstallerType();
            }
            else if (manifest.InstallerType.HasValue)
            {
                Type = manifest.InstallerType.Value.ToSDKInstallerType();
            }
            else if (Enum.TryParse<InstallerType>(Path.GetExtension(PackageUri.ToString())[1..], true, out var type))
            {
                Type = type;
            }
        }

        public override async Task<FileSystemInfo> DownloadAsync(DirectoryInfo folder = null)
        {
            // Find the package URI
            await PopulatePackageUri();
            if (!Status.IsAtLeast(PackageStatus.DownloadReady))
                return null;

            // Download package
            await StorageHelper.BackgroundDownloadPackage(this, PackageUri, folder);
            if (!Status.IsAtLeast(PackageStatus.Downloaded))
                return null;

            // Set the proper file name
            DownloadItem = ((FileInfo)DownloadItem).CopyRename(Path.GetFileName(PackageUri.ToString()));

            WeakReferenceMessenger.Default.Send(SuccessMessage.CreateForPackageDownloadCompleted(this));
            Status = PackageStatus.Downloaded;
            return DownloadItem;
        }

        private async Task PopulatePackageUri()
        {
            WeakReferenceMessenger.Default.Send(new PackageFetchStartedMessage(this));
            try
            {
                if (PackageUri == null)
                    Update(await WinGetApi.GetManifest(Urn.GetContent<NamespaceSpecificString>().UnEscapedValue, Version));

                WeakReferenceMessenger.Default.Send(new SuccessMessage(null, this, SuccessType.PackageFetchCompleted));
                Status = PackageStatus.DownloadReady;
            }
            catch (Exception ex)
            {
                WeakReferenceMessenger.Default.Send(new ErrorMessage(ex, this, ErrorType.PackageFetchFailed));
            }
        }

        public override async Task<ImageBase> CacheAppIcon()
        {
            ImageBase icon = null;
            if (Model?.IconUrl != null)
            {
                icon = new FileImage
                {
                    Url = Model.IconUrl,
                    ImageType = ImageType.Logo
                };
            }

            return icon ?? TextImage.CreateFromName(Model?.Latest?.Name ?? Title);
        }

        public override async Task<ImageBase> CacheHeroImage()
        {
            return null;
        }

        public override async Task<List<ImageBase>> CacheScreenshots()
        {
            return new List<ImageBase>();
        }

        public override async Task<bool> InstallAsync()
        {
            // Make sure installer is downloaded
            Guard.IsTrue(Status.IsAtLeast(PackageStatus.Downloaded), nameof(Status));
            bool isSuccess = false;

            // Get installer for current architecture
            var sysArch = Win32Helper.GetSystemArchitecture();
            Installer = Manifest.Installers.Find(i => sysArch == i.Arch.ToSDKArch());
            if (Installer == null)
                Installer = Manifest.Installers.Find(i => i.Arch == WinGetRun.Enums.InstallerArchitecture.X86
                    || i.Arch == WinGetRun.Enums.InstallerArchitecture.Neutral);
            if (Installer == null)
            {
                string archStr = string.Join(", ", Manifest.Installers.Select(i => i.Arch));
                throw new PlatformNotSupportedException($"Your computer's architecture is {sysArch}, which is not supported by this package. " +
                    $"This package supports {archStr}.");
            }

            switch (Type.Reduce())
            {
                case InstallerType.Msix:
                    isSuccess = await PackagedInstallerHelper.Install(this);
                    var file = (FileInfo)DownloadItem;
                    PackagedInstallerType = PackagedInstallerHelper.GetInstallerType(file);
                    PackageFamilyName = PackagedInstallerHelper.GetPackageFamilyName(file, PackagedInstallerType.Value.HasFlag(InstallerType.Bundle));
                    break;

                default:
                    var args = Installer.Switches?.Silent ?? Manifest.Switches?.Silent;
                    isSuccess = await Win32Helper.Install(this, args);
                    break;
            }

            if (isSuccess)
                Status = PackageStatus.Installed;
            return isSuccess;
        }

        public override async Task<bool> CanLaunchAsync()
        {
            if (HasPackageFamilyName)
                return await PackagedInstallerHelper.IsInstalled(PackageFamilyName);

            return false;
        }

        public override async Task LaunchAsync()
        {
            switch (Installer.InstallerType ?? Manifest.InstallerType)
            {
                case WinGetRun.Enums.InstallerType.Appx:
                case WinGetRun.Enums.InstallerType.Msix:
                    Guard.IsTrue(HasPackageFamilyName, nameof(HasPackageFamilyName));
                    await PackagedInstallerHelper.Launch(PackageFamilyName);
                    break;
            }
        }

        private string _PackageFamilyName;
        public string PackageFamilyName
        {
            get => _PackageFamilyName;
            set => SetProperty(ref _PackageFamilyName, value);
        }
        public bool HasPackageFamilyName => PackageFamilyName != null;

        private InstallerType? _PackagedInstallerType;
        public InstallerType? PackagedInstallerType
        {
            get => _PackagedInstallerType;
            set => SetProperty(ref _PackagedInstallerType, value);
        }
        public bool HasPackagedInstallerType => PackagedInstallerType == null;

        private string _PackageId;
        public string PackageId
        {
            get => _PackageId;
            set => SetProperty(ref _PackageId, value);
        }

        private Manifest _Manifest;
        public Manifest Manifest
        {
            get => _Manifest;
            set => SetProperty(ref _Manifest, value);
        }

        private Installer _Installer;
        public Installer Installer
        {
            get => _Installer;
            set => SetProperty(ref _Installer, value);
        }
    }
}
