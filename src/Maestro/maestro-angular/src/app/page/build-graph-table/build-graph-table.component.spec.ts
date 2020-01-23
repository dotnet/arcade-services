import { async, ComponentFixture, TestBed } from '@angular/core/testing';

import { BuildGraphTableComponent } from './build-graph-table.component';
import { NO_ERRORS_SCHEMA, Pipe, PipeTransform } from '@angular/core';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { MockCommitLink, MockBuildLink } from "src/app/mock-pipes.spec";

describe('BuildGraphTableComponent', () => {
  let component: BuildGraphTableComponent;
  let fixture: ComponentFixture<BuildGraphTableComponent>;

  beforeEach(async(() => {
    TestBed.configureTestingModule({
      declarations: [
        BuildGraphTableComponent,
        MockCommitLink,
        MockBuildLink,
      ],
      imports: [
        NoopAnimationsModule,
      ],
      schemas: [
        NO_ERRORS_SCHEMA, // Allow unrecognized elements (other components we don't want to test)
      ],
    })
    .compileComponents();
  }));

  beforeEach(() => {
    fixture = TestBed.createComponent(BuildGraphTableComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
