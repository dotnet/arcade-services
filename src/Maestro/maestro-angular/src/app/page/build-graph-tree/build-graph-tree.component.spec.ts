import { async, ComponentFixture, TestBed } from '@angular/core/testing';

import { BuildGraphTreeComponent } from './build-graph-tree.component';
import { NO_ERRORS_SCHEMA, } from '@angular/core';
import { MockCommitLink, MockBuildLink, MockGetRepositoryName, MockRepoName} from "src/app/mock-pipes.spec";

describe('BuildGraphTreeComponent', () => {
  let component: BuildGraphTreeComponent;
  let fixture: ComponentFixture<BuildGraphTreeComponent>;

  beforeEach(async(() => {
    TestBed.configureTestingModule({
      declarations: [
        BuildGraphTreeComponent,
        MockCommitLink,
        MockBuildLink,
        MockGetRepositoryName,
        MockRepoName,
      ],
      schemas: [ NO_ERRORS_SCHEMA ],
    })
    .compileComponents();
  }));

  beforeEach(() => {
    fixture = TestBed.createComponent(BuildGraphTreeComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
