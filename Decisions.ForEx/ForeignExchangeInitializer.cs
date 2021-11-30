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
                ORM<ForeignExchangeSettings> orm = new ORM<ForeignExchangeSettings>();
                ModuleSettingsAccessor<ForeignExchangeSettings>.SaveSettings();
                string sqlUpdate =
                    $@"UPDATE {newSettingsTableName} SET user_id=ol.user_id, password=ol.password, api_key=ol.api_key, use_https=ol.use_https FROM {oldSettingsTableName} as ol WHERE {newSettingsTableName}.entity_name=ol.entity_name;";
                orm.RunQuery(sqlUpdate);
                string sqlRemove = $@"ALTER TABLE entity_header_data DISABLE TRIGGER entity_header_data_delete_trigger;
                                    DELETE FROM entity_header_data WHERE entity_type_short_name='{oldSettingsClassName}';
                                    DROP TABLE {oldSettingsTableName};
                                    ALTER TABLE entity_header_data ENABLE TRIGGER entity_header_data_delete_trigger;";
                orm.RunQuery(sqlRemove);
            }
        }
    }
}