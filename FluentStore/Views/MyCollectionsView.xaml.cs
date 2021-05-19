﻿using FluentStore.ViewModels;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=234238

namespace FluentStore.Views
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    [Helpers.RequiresSignIn]
    public sealed partial class MyCollectionsView : Page
    {
        public MyCollectionsViewModel ViewModel
        {
            get => (MyCollectionsViewModel)GetValue(ViewModelProperty);
            set => SetValue(ViewModelProperty, value);
        }
        public static readonly DependencyProperty ViewModelProperty =
            DependencyProperty.Register(nameof(ViewModel), typeof(MyCollectionsViewModel), typeof(MyCollectionsView), new PropertyMetadata(new MyCollectionsViewModel()));

        public MyCollectionsView()
        {
            this.InitializeComponent();
        }
    }
}