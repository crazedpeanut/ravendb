﻿import {autocomplete} from "../autocompleteUtils";


describe("can complete select", function () {
    it("can complete fields - first field", async () => {
        const suggestions = await autocomplete("from Orders select | ");
        
        const expectedFields = ["Company", "Employee"];
        
        for (const field of expectedFields) {
            expect(suggestions.find(x => x.value.startsWith(field)))
                .toBeTruthy();
        }
    });

    it("can complete fields - nested field - just after dot", async () => {
        const suggestions = await autocomplete("from Orders select Lines[].| ");

        const expectedFields = ["Discount", "Product"];

        for (const field of expectedFields) {
            expect(suggestions.find(x => x.value.startsWith(field)))
                .toBeTruthy();
        }
    });

    it("can complete fields - nested field - partial", async () => {
        const suggestions = await autocomplete("from Orders select Lines[].Di| ");

        const expectedFields = ["Discount"];

        for (const field of expectedFields) {
            expect(suggestions.find(x => x.value.startsWith(field)))
                .toBeTruthy();
        }
    });

    it("can complete fields - next field", async () => {
        const suggestions = await autocomplete("from Orders select Field1,  | ");

        const expectedFields = ["Company", "Employee"];

        for (const field of expectedFields) {
            expect(suggestions.find(x => x.value.startsWith(field)))
                .toBeTruthy();
        }
    });
    
    it("doesn't complete keywords in select", async () => {
        const nextKeywords = ["limit", "include"];

        const suggestions = await autocomplete("from Orders select  | ");

        for (const keyword of nextKeywords) {
            expect(suggestions.find(x => x.value.startsWith(keyword)))
                .toBeFalsy();
        }
    });
    
    it("has empty list when defining select as alias", async () => {
        const suggestions = await autocomplete("from Orders select Company as |");
        
        expect(suggestions)
            .toBeEmpty();
    });
});
