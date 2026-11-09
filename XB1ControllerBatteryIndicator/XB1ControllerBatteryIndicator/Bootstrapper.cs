using System;
using System.Collections.Generic;
using System.Windows;
using Caliburn.Micro;

namespace XB1ControllerBatteryIndicator
{
    internal class Bootstrapper : BootstrapperBase
    {
        private readonly SimpleContainer _container = new SimpleContainer();

        public Bootstrapper()
        {
            Initialize();
        }

        protected override void Configure()
        {
            _container.Singleton<IWindowManager, WindowManager>();
            _container.PerRequest<SystemTrayViewModel>();
        }

        protected override object GetInstance(Type service, string key)
        {
            return _container.GetInstance(service, key);
        }

        protected override IEnumerable<object> GetAllInstances(Type service)
        {
            return _container.GetAllInstances(service);
        }

        protected override void BuildUp(object instance)
        {
            _container.BuildUp(instance);
        }

        protected override async void OnStartup(object sender, StartupEventArgs e)
        {
            //before any view or view model reads the settings
            SettingsMigration.Run();

            base.OnStartup(sender, e);

            await DisplayRootViewForAsync<SystemTrayViewModel>();
        }
    }
}
