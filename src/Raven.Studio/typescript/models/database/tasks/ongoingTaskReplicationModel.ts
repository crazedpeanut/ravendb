﻿/// <reference path="../../../../typings/tsd.d.ts"/>
import appUrl = require("common/appUrl");
import router = require("plugins/router");
import ongoingTask = require("models/database/tasks/ongoingTaskModel"); 
import clusterTopologyManager = require("common/shell/clusterTopologyManager");

class ongoingTaskReplicationModel extends ongoingTask {
    editUrl: KnockoutComputed<string>;

    destinationDB = ko.observable<string>();
    destinationURL = ko.observable<string>();
  
    validationGroup: KnockoutValidationGroup;

    constructor(dto: Raven.Client.ServerWide.Operations.OngoingTaskReplication, isEdit: boolean) {
        super();

        this.isEdit = isEdit;
        this.update(dto); 
        this.initializeObservables();
        this.initValidation();
    }
    
    initializeObservables() {
        super.initializeObservables();

        const urls = appUrl.forCurrentDatabase();
        this.editUrl = urls.editExternalReplication(this.taskId); 
    }

    update(dto: Raven.Client.ServerWide.Operations.OngoingTaskReplication) {
        super.update(dto);
        this.destinationDB(dto.DestinationDatabase);
        this.destinationURL(dto.DestinationUrl);
    }

    editTask() {
        router.navigate(this.editUrl());
    }

    toDto(): externalReplicationDataFromUI {
        return {
            TaskName: this.taskName(),
            DestinationURL: this.destinationURL(),
            DestinationDB: this.destinationDB()
        };
    }

    initValidation() {

        this.destinationDB.extend({
            required: true,
            validDatabaseName: true
        });

        this.destinationURL.extend({
            required: true,
            validUrl: true
        });

        this.validationGroup = ko.validatedObservable({
            destinationDB: this.destinationDB,
            destinationURL: this.destinationURL
        });
    }

    static empty(): ongoingTaskReplicationModel {
        return new ongoingTaskReplicationModel({
            TaskName: "",
            TaskType: "Replication",
            DestinationDatabase: null,
            DestinationUrl: clusterTopologyManager.default.localNodeUrl()
        } as Raven.Client.ServerWide.Operations.OngoingTaskReplication, true);
    }

    protected generateTaskName(dto: Raven.Client.ServerWide.Operations.OngoingTaskReplication): string {
        return `External replication to ${dto.DestinationDatabase}@${dto.DestinationUrl}`;
    }
}

export = ongoingTaskReplicationModel;
