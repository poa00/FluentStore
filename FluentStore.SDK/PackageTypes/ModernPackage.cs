﻿using FluentStore.SDK.Helpers;
using FluentStore.SDK.Images;
using FluentStore.SDK.Models;
using CommunityToolkit.Diagnostics;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IO;

namespace FluentStore.SDK.Packages
{
    public class ModernPackage<TModel> : GenericPackage<TModel>
    {
        public ModernPackage(PackageHandlerBase packageHandler) : base(packageHandler)
        {
        }

        public override bool Equals(PackageBase other)
        {
            if (other is ModernPackage<TModel> mpackage)
            {
                return mpackage.Type == this.Type && mpackage.PackageFamilyName == this.PackageFamilyName
                    && mpackage.Version == this.Version;
            }
            else
            {
                return base.Equals(other);
            }
        }

        public override bool RequiresDownloadForCompatCheck => true;
        public override async Task<string> GetCannotBeInstalledReason()
        {
            Guard.IsTrue(Status.IsAtLeast(PackageStatus.Downloaded), nameof(Status));
            return PackagedInstallerHelper.GetCannotBeInstalledReason(
                (FileInfo)DownloadItem, Type.HasFlag(InstallerType.Bundle));
        }

        public override async Task<bool> CanLaunchAsync()
        {
            Guard.IsNotNull(PackageFamilyName, nameof(PackageFamilyName));
            try
            {
                return await PackagedInstallerHelper.IsInstalled(PackageFamilyName);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error checking if {PackageFamilyName} is installed. Error:\r\n{ex}");
                return false;
            }
        }

        public override async Task<bool> InstallAsync()
        {
            // Make sure installer is downloaded
            Guard.IsTrue(Status.IsAtLeast(PackageStatus.Downloaded), nameof(Status));
            return await PackagedInstallerHelper.Install(this);
        }

        public override async Task LaunchAsync()
        {
            Guard.IsNotNull(PackageFamilyName, nameof(PackageFamilyName));
            await PackagedInstallerHelper.Launch(PackageFamilyName);
        }

        /// <summary>
        /// Determines the <see cref="InstallerType"/> of the downloaded package.
        /// Requires <see cref="PackageBase.Status"/> to be <see cref="PackageStatus.Downloaded"/>.
        /// </summary>
        /// <returns>The file extension that corresponds with the determined <see cref="InstallerType"/>.</returns>
        public async Task<string> GetInstallerType()
        {
            Guard.IsTrue(Status.IsAtLeast(PackageStatus.Downloaded), nameof(Status));

            if (Type == InstallerType.Unknown)
                Type = PackagedInstallerHelper.GetInstallerType((FileInfo)DownloadItem);
            return Type.GetExtension();
        }

        public override async Task<ImageBase> CacheAppIcon()
        {
            try
            {
                Guard.IsTrue(Status.IsAtLeast(PackageStatus.Downloaded), nameof(Status));
                return PackagedInstallerHelper.GetAppIcon(
                    (FileInfo)DownloadItem, Type.HasFlag(InstallerType.Bundle));
            }
            catch
            {
                return TextImage.CreateFromName(Title, ImageType.Logo);
            }
        }

        public override async Task<ImageBase> CacheHeroImage()
        {
            return null;
        }

        public override async Task<List<ImageBase>> CacheScreenshots()
        {
            return new List<ImageBase>(0);
        }

        private string _PackageFamilyName;
        public string PackageFamilyName
        {
            get => _PackageFamilyName;
            set => SetProperty(ref _PackageFamilyName, value);
        }

        private string _PublisherDisplayName;
        public string PublisherDisplayName
        {
            get => _PublisherDisplayName;
            set => SetProperty(ref _PublisherDisplayName, value);
        }

        private string _LogoRelativePath;
        public string LogoRelativePath
        {
            get => _LogoRelativePath;
            set => SetProperty(ref _LogoRelativePath, value);
        }
    }
}
