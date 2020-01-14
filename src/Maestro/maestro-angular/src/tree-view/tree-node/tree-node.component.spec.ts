import { async, ComponentFixture, TestBed } from '@angular/core/testing';

import { TreeNodeComponent } from './tree-node.component';
import { NO_ERRORS_SCHEMA } from '@angular/core';
import { StatefulModule } from 'src/stateful';

describe('TreeNodeComponent', () => {
  let component: TreeNodeComponent;
  let fixture: ComponentFixture<TreeNodeComponent>;

  beforeEach(async(() => {
    TestBed.configureTestingModule({
      declarations: [ TreeNodeComponent ],
      imports: [
        StatefulModule,
      ],
      schemas: [ NO_ERRORS_SCHEMA ],
    })
    .compileComponents();
  }));

  beforeEach(() => {
    fixture = TestBed.createComponent(TreeNodeComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
