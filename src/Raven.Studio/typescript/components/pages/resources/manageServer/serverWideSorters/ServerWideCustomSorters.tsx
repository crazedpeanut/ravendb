﻿import React from "react";
import { Col, Row } from "reactstrap";
import { AboutViewAnchored, AboutViewHeading, AccordionItemWrapper } from "components/common/AboutView";
import { Icon } from "components/common/Icon";
import { HrHeader } from "components/common/HrHeader";
import { useAsync } from "react-async-hook";
import { useAppUrls } from "components/hooks/useAppUrls";
import { useServices } from "components/hooks/useServices";
import { useAppSelector } from "components/store";
import { licenseSelectors } from "components/common/shell/licenseSlice";
import { CounterBadge } from "components/common/CounterBadge";
import AccordionCommunityLicenseLimited from "components/common/AccordionCommunityLicenseLimited";
import classNames from "classnames";
import SortersList from "./ServerWideCustomSortersList";

export default function ServerWideCustomSorters() {
    const { manageServerService } = useServices();
    const asyncGetSorters = useAsync(manageServerService.getServerWideCustomSorters, []);

    const { appUrl } = useAppUrls();

    const isCommunity = useAppSelector(licenseSelectors.licenseType) === "Community";
    const communityLimit = 5; // TODO get from license selector

    const resultsCount = asyncGetSorters.result?.length ?? null;
    const isAddDisabled = asyncGetSorters.status !== "success" || (isCommunity && resultsCount === communityLimit);

    return (
        <div className="content-margin">
            <Col xxl={12}>
                <Row className="gy-sm">
                    <Col>
                        <AboutViewHeading title="Server-Wide Sorters" icon="server-wide-custom-sorters" />
                        <a
                            href={appUrl.forEditServerWideCustomAnalyzer()}
                            className={classNames("btn btn-primary mb-3", { disabled: isAddDisabled })}
                        >
                            <Icon icon="plus" />
                            Add a server-wide custom sorter
                        </a>
                        <HrHeader
                            right={
                                <a href="https://ravendb.net/l/LGUJH8/6.0" target="_blank">
                                    <Icon icon="link" />
                                    Sorters tutorial
                                </a>
                            }
                        >
                            Server-wide custom sorters
                            {isCommunity && (
                                <CounterBadge className="ms-2" count={resultsCount} limit={communityLimit} />
                            )}
                        </HrHeader>
                        <SortersList
                            fetchStatus={asyncGetSorters.status}
                            sorters={asyncGetSorters.result}
                            reload={asyncGetSorters.execute}
                        />
                    </Col>
                    <Col sm={12} lg={4}>
                        <AboutViewAnchored>
                            <AccordionItemWrapper
                                targetId="1"
                                icon="about"
                                color="info"
                                description="Get additional info on what this feature can offer you"
                                heading="About this view"
                            >
                                Umm
                            </AccordionItemWrapper>
                            {isCommunity && (
                                <AccordionCommunityLicenseLimited
                                    targetId="licensing"
                                    featureName="Custom Sorters"
                                    featureIcon="custom-sorters"
                                    description="Upgrade to a paid plan and get unlimited availability."
                                />
                            )}
                        </AboutViewAnchored>
                    </Col>
                </Row>
            </Col>
        </div>
    );
}
