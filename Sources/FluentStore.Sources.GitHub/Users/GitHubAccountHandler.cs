﻿using CommunityToolkit.Diagnostics;
using CommunityToolkit.Mvvm.DependencyInjection;
using FluentStore.SDK.AbstractUI;
using FluentStore.SDK.AbstractUI.Models;
using FluentStore.SDK.Users;
using FluentStore.Services;
using Flurl;
using Octokit;
using OwlCore.AbstractUI.Models;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace FluentStore.Sources.GitHub.Users
{
    public class GitHubAccountHandler : AccountHandlerBase<GitHubAccount>
    {
        private const string ABSUI_ID_NAMEBOX = "nameBox";
        private const string ABSUI_ID_BIOBOX = "bioBox";
        private const string ABSUI_ID_COMPANYBOX = "companyBox";
        private const string ABSUI_ID_LOCATIONBOX = "locationBox";
        private readonly CredentialStore _credentialStore;
        private readonly GitHubClient _client;

        public GitHubAccountHandler(GitHubClient client, CredentialStore credentialStore, IPasswordVaultService passwordVaultService)
            : base(passwordVaultService)
        {
            _client = client;
            _credentialStore = credentialStore;
        }

        public override string Id => "gh-user";

        public override string DisplayName => "GitHub";

        private string Token { get; set; }

        private readonly string[] _scopes = new[]
        {
            "read:user", "user:email", "repo",
        };

        protected override async Task<SDK.Users.Account> UpdateCurrentUser()
        {
            var user = await _client.User.Current();
            return new GitHubAccount(user);
        }

        public override Task<bool> SignInAsync(CredentialBase credential) => SignInAsync(credential.Password);

        public async Task<bool> SignInAsync(string token)
        {
            try
            {
                Token = token;

                _credentialStore.Token = Token;
                CurrentUser = await UpdateCurrentUser();

                SaveCredential(Token);

                IsLoggedIn = true;
            }
            catch (Exception ex)
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine(ex.Message);
#endif

                Token = null;
                IsLoggedIn = false;
            }

            return IsLoggedIn;
        }

        public override Task SignOutAsync()
        {
            _credentialStore.Token = null;

            RemoveCredential(Token);

            IsLoggedIn = false;
            Token = null;
            CurrentUser = null;

            return Task.CompletedTask;
        }

        public override async Task HandleAuthActivation(Url url)
        {
            if (!url.QueryParams.TryGetFirst("code", out var code))
                ThrowHelper.ThrowInvalidOperationException("No OAuth code was supplied.");

            Octokit.OauthTokenRequest request = new(Secrets.GH_CLIENTID, Secrets.GH_CLIENTSECRET, code.ToString());
            var response = await _client.Oauth.CreateAccessToken(request);

            await SignInAsync(response.AccessToken);
        }

        public override AbstractForm CreateSignInForm()
        {
            return AbstractUIHelper.CreateSingleButtonForm("SignInCollection", "Click the button below to sign in with browser.", "Sign in",
                async (sender, e) =>
                {
                    // Generate start URL
                    // https://docs.github.com/en/developers/apps/building-oauth-apps/authorizing-oauth-apps#1-request-a-users-github-identity
                    OauthLoginRequest request = new(Secrets.GH_CLIENTID)
                    {
                        RedirectUri = GetAuthProtocolUrl().ToUri()
                    };
                    foreach (string scope in _scopes)
                        request.Scopes.Add(scope);
                    var uri = _client.Oauth.GetGitHubLoginUrl(request);

                    INavigationService navService = Ioc.Default.GetRequiredService<INavigationService>();
                    await navService.OpenInBrowser(uri);
                });
        }

        public override AbstractForm CreateSignUpForm()
        {
            throw new NotImplementedException();
        }

        public override AbstractForm CreateManageAccountForm()
        {
            return AbstractUIHelper.CreateOpenInBrowserForm("ManageCollection", "Manage your account on the website.",
                GetAccount().GitHubUser.HtmlUrl, Ioc.Default.GetRequiredService<INavigationService>());

            // FIXME: The update call returns HTTP 404
            AbstractForm form = new("ManageCollection", onSubmit: ManageButton_Clicked)
            {
                new AbstractTextBox(ABSUI_ID_NAMEBOX, DisplayName, "Name"),
                new AbstractTextBox(ABSUI_ID_BIOBOX, GetAccount().GitHubUser.Bio, "Bio"),
                new AbstractTextBox(ABSUI_ID_COMPANYBOX, GetAccount().GitHubUser.Company, "Company"),
                new AbstractTextBox(ABSUI_ID_LOCATIONBOX, GetAccount().GitHubUser.Location, "Location"),
            };
            return form;
        }

        private async void ManageButton_Clicked(object sender, EventArgs e)
        {
            if (sender is not AbstractForm form)
                return;

            UserUpdate update = new();

            foreach (AbstractTextBox box in form.Where(elem => elem is AbstractTextBox))
            {
                string val = box.Value;
                switch (box.Id)
                {
                    case ABSUI_ID_NAMEBOX:
                        update.Name = val;
                        break;

                    case ABSUI_ID_BIOBOX:
                        update.Bio = val;
                        break;

                    case ABSUI_ID_COMPANYBOX:
                        update.Company = val;
                        break;

                    case ABSUI_ID_LOCATIONBOX:
                        update.Location = val;
                        break;
                }
            }

            _ = await _client.User.Update(update);
        }
    }
}
