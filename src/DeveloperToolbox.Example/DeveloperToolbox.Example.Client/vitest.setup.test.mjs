describe("accessibility matcher", () => {
  it("accepts_aResultWithoutViolations", () => {
    expect({ violations: [] }).toHaveNoViolations();
  });

  it("reports_eachViolationHelpMessage", () => {
    expect(() =>
      expect({
        violations: [{ help: "Add a label" }, { help: "Improve contrast" }],
      }).toHaveNoViolations(),
    ).toThrow(/Add a label.*Improve contrast/s);
  });
});
