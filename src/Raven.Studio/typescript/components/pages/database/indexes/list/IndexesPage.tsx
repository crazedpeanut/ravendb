﻿import React from "react";
import database from "models/resources/database";
import IndexFilter from "./IndexFilter";
import IndexSelectActions from "./IndexSelectActions";
import IndexUtils from "../../../../utils/IndexUtils";
import { useAccessManager } from "hooks/useAccessManager";
import { useAppUrls } from "hooks/useAppUrls";
import "./IndexesPage.scss";
import { Button, Col, Row, UncontrolledPopover } from "reactstrap";
import { LoadingView } from "components/common/LoadingView";
import { StickyHeader } from "components/common/StickyHeader";
import { BulkIndexOperationConfirm } from "components/pages/database/indexes/list/BulkIndexOperationConfirm";
import { ConfirmResetIndex } from "components/pages/database/indexes/list/ConfirmResetIndex";
import { getAllIndexes, useIndexesPage } from "components/pages/database/indexes/list/useIndexesPage";
import { useEventsCollector } from "hooks/useEventsCollector";
import { NoIndexes } from "components/pages/database/indexes/list/partials/NoIndexes";
import { Icon } from "components/common/Icon";
import { ConfirmSwapSideBySideIndex } from "./ConfirmSwapSideBySideIndex";
import ActionContextUtils from "components/utils/actionContextUtils";
import { getLicenseLimitReachStatus } from "components/utils/licenseLimitsUtils";
import { useAppSelector } from "components/store";
import { licenseSelectors } from "components/common/shell/licenseSlice";
import { useRavenLink } from "components/hooks/useRavenLink";
import IndexesPageList, { IndexesPageListProps } from "./IndexesPageList";
import IndexesPageLicenseLimits from "./IndexesPageLicenseLimits";
import IndexesPageAboutView from "./IndexesPageAboutView";

interface IndexesPageProps {
    db: database;
    stale?: boolean;
    indexName?: string;
}

export function IndexesPage(props: IndexesPageProps) {
    const { db, stale, indexName: indexToHighlight } = props;

    const { canReadWriteDatabase } = useAccessManager();
    const { reportEvent } = useEventsCollector();

    const { forCurrentDatabase: urls } = useAppUrls();
    const newIndexUrl = urls.newIndex();

    const {
        loading,
        bulkOperationConfirm,
        setBulkOperationConfirm,
        resetIndexData,
        swapSideBySideData,
        stats,
        selectedIndexes,
        toggleSelectAll,
        onSelectCancel,
        filter,
        setFilter,
        filterByStatusOptions,
        filterByTypeOptions,
        regularIndexes,
        groups,
        replacements,
        highlightCallback,
        confirmSetLockModeSelectedIndexes,
        allIndexesCount,
        setIndexPriority,
        startIndexes,
        disableIndexes,
        pauseIndexes,
        setIndexLockMode,
        toggleSelection,
        openFaulty,
        getSelectedIndexes,
        confirmDeleteIndexes,
        globalIndexingStatus,
    } = useIndexesPage(db, stale);

    const deleteSelectedIndexes = () => {
        reportEvent("indexes", "delete-selected");
        return confirmDeleteIndexes(db, getSelectedIndexes());
    };

    const startSelectedIndexes = () => startIndexes(getSelectedIndexes());
    const disableSelectedIndexes = () => disableIndexes(getSelectedIndexes());
    const pauseSelectedIndexes = () => pauseIndexes(getSelectedIndexes());

    const indexNames = getAllIndexes(groups, replacements).map((x) => x.name);

    const allActionContexts = ActionContextUtils.getContexts(db.getLocations());

    const upgradeLicenseLink = useRavenLink({ hash: "FLDLO4", isDocs: false });

    const autoClusterLimit = useAppSelector(licenseSelectors.statusValue("MaxNumberOfAutoIndexesPerCluster"));
    const staticClusterLimit = useAppSelector(licenseSelectors.statusValue("MaxNumberOfStaticIndexesPerCluster"));
    const autoDatabaseLimit = useAppSelector(licenseSelectors.statusValue("MaxNumberOfAutoIndexesPerDatabase"));
    const staticDatabaseLimit = useAppSelector(licenseSelectors.statusValue("MaxNumberOfStaticIndexesPerDatabase"));

    const autoClusterCount = useAppSelector(licenseSelectors.limitsUsage).NumberOfAutoIndexesInCluster;
    const staticClusterCount = useAppSelector(licenseSelectors.limitsUsage).NumberOfStaticIndexesInCluster;

    const autoDatabaseCount = stats.indexes.filter((x) => IndexUtils.isAutoIndex(x)).length;
    const staticDatabaseCount = stats.indexes.length - autoDatabaseCount;

    const autoClusterLimitStatus = getLicenseLimitReachStatus(autoClusterCount, autoClusterLimit);
    const staticClusterLimitStatus = getLicenseLimitReachStatus(staticClusterCount, staticClusterLimit);

    const autoDatabaseLimitStatus = getLicenseLimitReachStatus(autoDatabaseCount, autoDatabaseLimit);
    const staticDatabaseLimitStatus = getLicenseLimitReachStatus(staticDatabaseCount, staticDatabaseLimit);

    const isNewIndexDisabled =
        staticClusterLimitStatus === "limitReached" || staticDatabaseLimitStatus === "limitReached";

    if (loading) {
        return <LoadingView />;
    }

    if (stats.indexes.length === 0) {
        return <NoIndexes database={db} />;
    }

    const indexesPageListCommonProps: Omit<IndexesPageListProps, "indexes"> = {
        db,
        replacements,
        selectedIndexes,
        indexToHighlight,
        globalIndexingStatus,
        resetIndexData,
        swapSideBySideData,
        setIndexPriority,
        setIndexLockMode,
        openFaulty,
        startIndexes,
        disableIndexes,
        pauseIndexes,
        confirmDeleteIndexes,
        toggleSelection,
        highlightCallback,
    };

    return (
        <>
            <IndexesPageLicenseLimits
                staticClusterLimitStatus={staticClusterLimitStatus}
                staticClusterCount={staticClusterCount}
                staticClusterLimit={staticClusterLimit}
                upgradeLicenseLink={upgradeLicenseLink}
                autoClusterLimitStatus={autoClusterLimitStatus}
                autoClusterCount={autoClusterCount}
                autoClusterLimit={autoClusterLimit}
                staticDatabaseLimitStatus={staticDatabaseLimitStatus}
                staticDatabaseCount={staticDatabaseCount}
                staticDatabaseLimit={staticDatabaseLimit}
                autoDatabaseLimitStatus={autoDatabaseLimitStatus}
                autoDatabaseCount={autoDatabaseCount}
                autoDatabaseLimit={autoDatabaseLimit}
            />

            {stats.indexes.length > 0 && (
                <StickyHeader>
                    <Row>
                        <Col className="hstack">
                            <div id="NewIndexButton">
                                <Button
                                    color="primary"
                                    href={newIndexUrl}
                                    disabled={isNewIndexDisabled}
                                    className="rounded-pill px-3 pe-auto"
                                >
                                    <Icon icon="index" addon="plus" />
                                    <span>New index</span>
                                </Button>
                            </div>

                            {isNewIndexDisabled && (
                                <UncontrolledPopover
                                    trigger="hover"
                                    target="NewIndexButton"
                                    placement="top"
                                    className="bs5"
                                >
                                    <div className="p-3 text-center">
                                        <Icon
                                            icon={staticClusterLimitStatus === "limitReached" ? "cluster" : "database"}
                                        />
                                        {staticClusterLimitStatus === "limitReached" ? "Cluster" : "Database"} has
                                        reached the maximum number of static indexes allowed per{" "}
                                        {staticClusterLimitStatus === "limitReached" ? "cluster" : "database"} by your
                                        license.
                                        <br />
                                        Delete unused indexes or{" "}
                                        <strong>
                                            <a href={upgradeLicenseLink} target="_blank">
                                                upgrade your license
                                            </a>
                                        </strong>
                                    </div>
                                </UncontrolledPopover>
                            )}
                        </Col>
                        <Col xs="auto">
                            <IndexesPageAboutView
                                isUnlimited={
                                    staticClusterLimitStatus === "notReached" &&
                                    staticDatabaseLimitStatus === "notReached"
                                }
                            />
                        </Col>
                    </Row>
                    <IndexFilter
                        filter={filter}
                        setFilter={(x) => setFilter(x)}
                        filterByStatusOptions={filterByStatusOptions}
                        filterByTypeOptions={filterByTypeOptions}
                        indexesCount={allIndexesCount}
                    />

                    {/*  TODO  <IndexGlobalIndexing /> */}

                    {canReadWriteDatabase(db) && (
                        <IndexSelectActions
                            indexNames={indexNames}
                            selectedIndexes={selectedIndexes}
                            deleteSelectedIndexes={deleteSelectedIndexes}
                            startSelectedIndexes={startSelectedIndexes}
                            disableSelectedIndexes={disableSelectedIndexes}
                            pauseSelectedIndexes={pauseSelectedIndexes}
                            setLockModeSelectedIndexes={confirmSetLockModeSelectedIndexes}
                            toggleSelectAll={toggleSelectAll}
                            onCancel={onSelectCancel}
                        />
                    )}
                </StickyHeader>
            )}
            <div className="indexes p-4 pt-0 no-transition">
                <div className="indexes-list">
                    {filter.groupBy === "None" && (
                        <IndexesPageList {...indexesPageListCommonProps} indexes={regularIndexes} />
                    )}
                    {filter.groupBy === "Collection" &&
                        groups.map((group) => {
                            return (
                                <div className="mb-4" key={"group-" + group.name}>
                                    <h2 className="mt-0" title={"Collection: " + group.name}>
                                        {group.name}
                                    </h2>
                                    <IndexesPageList {...indexesPageListCommonProps} indexes={group.indexes} />
                                </div>
                            );
                        })}
                </div>
            </div>

            {bulkOperationConfirm && (
                <BulkIndexOperationConfirm {...bulkOperationConfirm} toggle={() => setBulkOperationConfirm(null)} />
            )}
            {resetIndexData.indexName && (
                <ConfirmResetIndex
                    indexName={resetIndexData.indexName}
                    toggle={() => resetIndexData.setIndexName(null)}
                    onConfirm={(x) => resetIndexData.onConfirm(x)}
                    allActionContexts={allActionContexts}
                />
            )}
            {swapSideBySideData.indexName && (
                <ConfirmSwapSideBySideIndex
                    indexName={swapSideBySideData.indexName}
                    toggle={() => swapSideBySideData.setIndexName(null)}
                    onConfirm={(x) => swapSideBySideData.onConfirm(x)}
                    allActionContexts={allActionContexts}
                />
            )}
        </>
    );
}
