﻿import React from "react";
import { Meta, StoryObj } from "@storybook/react";
import { withStorybookContexts, withBootstrap5, databaseAccessArgType, licenseArgType } from "test/storybookTestUtils";
import ConnectionStrings from "./ConnectionStrings";
import { DatabasesStubs } from "test/stubs/DatabasesStubs";
import { mockStore } from "test/mocks/store/MockStore";
import { mockServices } from "test/mocks/services/MockServices";
import { SharedStubs } from "test/stubs/SharedStubs";

export default {
    title: "Pages/Database/Settings",
    decorators: [withStorybookContexts, withBootstrap5],
    argTypes: {
        licenseType: licenseArgType,
        databaseAccess: databaseAccessArgType,
    },
} satisfies Meta;

interface DefaultConnectionStringsProps {
    isEmpty: boolean;
    isTestSuccess: boolean;
    isSecuredServer: boolean;
    licenseType: Raven.Server.Commercial.LicenseType;
    databaseAccess: databaseAccessLevel;
    hasRavenEtl: boolean;
    hasSqlEtl: boolean;
    hasOlapEtl: boolean;
    hasElasticSearchEtl: boolean;
    hasQueueEtl: boolean;
}

export const DefaultConnectionStrings: StoryObj<DefaultConnectionStringsProps> = {
    name: "Connection Strings",
    render: (props: DefaultConnectionStringsProps) => {
        const { accessManager, license } = mockStore;
        const { tasksService } = mockServices;

        accessManager.with_secureServer(props.isSecuredServer);

        tasksService.withLocalFolderPathOptions();
        tasksService.withBackupLocation();
        tasksService.withGetTasks((x) => {
            (
                x.OngoingTasks.find(
                    (x) => x.TaskType === "RavenEtl"
                ) as Raven.Client.Documents.Operations.OngoingTasks.OngoingTaskRavenEtl
            ).ConnectionStringName = Object.keys(DatabasesStubs.connectionStrings().RavenConnectionStrings)[0];
        });

        mockTestResults(props.isTestSuccess);

        tasksService.withConnectionStrings(
            props.isEmpty ? DatabasesStubs.emptyConnectionStrings() : DatabasesStubs.connectionStrings()
        );

        accessManager.with_securityClearance("ValidUser");
        accessManager.with_databaseAccess({
            [db.name]: props.databaseAccess,
        });

        license.with_LicenseLimited({
            Type: props.licenseType,
            HasRavenEtl: props.hasRavenEtl,
            HasSqlEtl: props.hasSqlEtl,
            HasOlapEtl: props.hasOlapEtl,
            HasElasticSearchEtl: props.hasElasticSearchEtl,
            HasQueueEtl: props.hasQueueEtl,
        });

        return <ConnectionStrings db={db} />;
    },
    args: {
        isEmpty: false,
        isTestSuccess: true,
        isSecuredServer: true,
        licenseType: "Enterprise",
        databaseAccess: "DatabaseAdmin",
        hasRavenEtl: true,
        hasSqlEtl: true,
        hasOlapEtl: true,
        hasElasticSearchEtl: true,
        hasQueueEtl: true,
    },
};

const db = DatabasesStubs.nonShardedClusterDatabase();

function mockTestResults(isSuccess: boolean) {
    const { tasksService, manageServerService } = mockServices;

    if (isSuccess) {
        tasksService.withTestClusterNodeConnection();
        tasksService.withTestSqlConnectionString();
        tasksService.withTestKafkaServerConnection();
        tasksService.withTestRabbitMqServerConnection();
        tasksService.withTestElasticSearchNodeConnection();
        manageServerService.withTestPeriodicBackupCredentials();
    } else {
        tasksService.withTestClusterNodeConnection(SharedStubs.nodeConnectionTestErrorResult());
        tasksService.withTestSqlConnectionString(SharedStubs.nodeConnectionTestErrorResult());
        tasksService.withTestKafkaServerConnection(SharedStubs.nodeConnectionTestErrorResult());
        tasksService.withTestRabbitMqServerConnection(SharedStubs.nodeConnectionTestErrorResult());
        tasksService.withTestElasticSearchNodeConnection(SharedStubs.nodeConnectionTestErrorResult());
        manageServerService.withTestPeriodicBackupCredentials(SharedStubs.nodeConnectionTestErrorResult());
    }
}
