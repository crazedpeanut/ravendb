﻿import React from "react";
import { Meta } from "@storybook/react";
import { withStorybookContexts, withBootstrap5 } from "test/storybookTestUtils";
import DatabaseCustomSorters from "./DatabaseCustomSorters";
import { DatabasesStubs } from "test/stubs/DatabasesStubs";
import { mockStore } from "test/mocks/store/MockStore";
import { mockServices } from "test/mocks/services/MockServices";
import { ManageServerStubs } from "test/stubs/ManageServerStubs";

export default {
    title: "Pages/Database/Settings/Custom Sorters",
    component: DatabaseCustomSorters,
    decorators: [withStorybookContexts, withBootstrap5],
} satisfies Meta<typeof DatabaseCustomSorters>;

const db = DatabasesStubs.nonShardedClusterDatabase();

function commonInit() {
    const { manageServerService } = mockServices;

    manageServerService.withServerWideCustomSorters();
}

export function NoLimits() {
    commonInit();

    const { databasesService } = mockServices;
    databasesService.withCustomSorters([
        ...DatabasesStubs.customSorters(),
        ManageServerStubs.serverWideCustomSorters()[0],
    ]);

    const { license } = mockStore;
    license.with_Enterprise();

    return <DatabaseCustomSorters db={db} />;
}

export function CommunityLimits() {
    commonInit();

    const { databasesService } = mockServices;
    databasesService.withCustomSorters();

    const { license } = mockStore;
    license.with_Community();

    return <DatabaseCustomSorters db={db} />;
}
