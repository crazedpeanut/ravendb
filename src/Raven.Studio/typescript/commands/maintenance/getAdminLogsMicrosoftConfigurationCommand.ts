import commandBase = require("commands/commandBase");
import endpoints = require("endpoints");

class getAdminLogsMicrosoftConfigurationCommand extends commandBase {
    
    execute(): JQueryPromise<object> {
        const url = endpoints.global.adminLogs.adminLogsMicrosoftConfiguration;
        
        return this.query<object>(url, null)
            .fail((response: JQueryXHR) => this.reportError("Failed to get Microsoft logs configuration", response.responseText, response.statusText)) 
    }
}

export = getAdminLogsMicrosoftConfigurationCommand;
