using System.Collections.Generic;
using DecisionsFramework;
using DecisionsFramework.Data.ORMapper;
using DecisionsFramework.Design.Properties;
using DecisionsFramework.Design.Properties.Attributes;
using DecisionsFramework.ServiceLayer;
using DecisionsFramework.ServiceLayer.Actions;
using DecisionsFramework.ServiceLayer.Actions.Common;
using DecisionsFramework.ServiceLayer.Services.Accounts;
using DecisionsFramework.ServiceLayer.Services.Administration;
using DecisionsFramework.ServiceLayer.Services.Folder;
using DecisionsFramework.ServiceLayer.Utilities;

namespace Decisions.ForEx
{
    [ORMEntity("foreign_exchange_settings")]

    public class ForeignExchangeSettings: AbstractModuleSettings, IValidationSource
    {
        [ORMField]
        private string userId;

        [ORMField]
        private string password;
        
        [ORMField] 
        private string apiKey;

        [ORMField] 
        private bool useHttps;

        public ForeignExchangeSettings()
        {
            this.EntityName = "Foreign Exchange Settings";
        }

        #region CurrencyLayer Properties
        [PropertyClassification(new[] {"CurrencyLayer.com Configuration"}, "CurrencyLayer.com API Key", 1, false)]
        public string ApiKey
        {
            get { return apiKey; }
            set { apiKey = value; }
        }

        [PropertyClassification(new[] {"CurrencyLayer.com Configuration"},
            "Use HTTPS", 2, false)]
        public bool UseHttps
        {
            get { return useHttps; }
            set { useHttps = value; }
        }
        #endregion
        
        #region StrikeIron Properties
        [PropertyHidden]
        [PropertyClassification(new [] {"StrikeIron.com Credentials (Deprecated)"}, "User Id", 8, true)]
        public string UserId
        {
            get { return userId; }
            set { userId = value; }
        }

        [PropertyHidden]
        [PropertyClassification(new [] {"StrikeIron.com Credentials (Deprecated)"}, "Password", 9, true)]        
        [PasswordText]
        public string Password
        {
            get { return password; }
            set { password = value; }
        }
        
        [ReadonlyEditor]
        [PropertyClassification(new string[] { "CurrencyLayer.com Configuration" }, "StrikeIron Users", 0, false)]
        public string DeprecationMessage
        {
            get
            {
                return "The StrikeIron Exchange Rate API has been deprecated in Decisions. See CurrencyLayer.com for information on creating an account for use with the new service.";
            }
            set { }
        }
        #endregion
        
        public override BaseActionType[] GetActions(AbstractUserContext userContext, EntityActionType[] types)
        {
            Account userAccount = userContext.GetAccount();

            FolderPermission permission = FolderService.Instance.GetAccountEffectivePermission(
                new SystemUserContext(), this.EntityFolderID, userAccount.AccountID);

            bool canAdministrate = FolderPermission.CanAdministrate == (FolderPermission.CanAdministrate & permission) ||
                                    userAccount.GetUserRights<PortalAdministratorModuleRight>() != null ||
                                    userAccount.IsAdministrator();

            if (canAdministrate)
                return new BaseActionType[] { new EditEntityAction(typeof(ForeignExchangeSettings), "Edit", "") { IsDefaultGridAction = true } };
    
            return new BaseActionType[0];
        }

        public void Initialize()
        {
            // this will create it
            ModuleSettingsAccessor<ForeignExchangeSettings>.GetSettings();
        }
        
        public ValidationIssue[] GetValidationIssues()
        {
            List<ValidationIssue> issues = new List<ValidationIssue>();

            if (useHttps)
            {
                issues.Add(new ValidationIssue("HTTPS requires a CurrencyLayer subscription with the HTTPS feature enabled. Ensure this has been configured before enabling HTTPS here.", "HTTPS Account Warning", BreakLevel.Warning));
            }

            return issues.ToArray();
        }

    }
}