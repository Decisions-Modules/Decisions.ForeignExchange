using System.Data;
using System.Linq;
using DecisionsFramework;
using DecisionsFramework.Data.ORMapper;
using DecisionsFramework.ServiceLayer;
using DecisionsFramework.ServiceLayer.Services.Folder;
using DecisionsFramework.ServiceLayer.Utilities;

namespace Decisions.ForEx
{
    public class ForeignExchangeInitializer: IInitializable
    {
        private static Log log = new Log("Foreign Exchange");

        public void Initialize()
        {
            const string oldSettingsClassName = "ForiegnExchangeSettings";
            const string oldSettingsTableName = "foriegn_exchange_settings";
            const string newSettingsTableName = "foreign_exchange_settings";

            if (DynamicORM.DatabaseDriver.DoesTableExist(DynamicORM.ConnectionString, oldSettingsTableName))
            {
                ORM<ForiegnExchangeSettings> orm = new ORM<ForiegnExchangeSettings>();

                var oldSettings = orm.Fetch().FirstOrDefault();
                if (oldSettings != null)
                {
                    ForeignExchangeSettings fes = ModuleSettingsAccessor<ForeignExchangeSettings>.GetSettings();
                    fes.UserId = oldSettings.userId;
                    fes.Password = oldSettings.password;
                    fes.ApiKey = oldSettings.apiKey;
                    fes.UseHttps = oldSettings.useHttps;
                    ModuleSettingsAccessor<ForeignExchangeSettings>.SaveSettings();
                    
                    string sqlQuery = $@"ALTER TABLE entity_header_data DISABLE TRIGGER entity_header_data_delete_trigger;
                                        DELETE FROM entity_header_data WHERE entity_type_short_name='{oldSettingsClassName}';
                                        DROP TABLE {oldSettingsTableName};
                                        ALTER TABLE entity_header_data ENABLE TRIGGER entity_header_data_delete_trigger;";
                    orm.RunQuery(sqlQuery);
                }
            }
        }
    }
}