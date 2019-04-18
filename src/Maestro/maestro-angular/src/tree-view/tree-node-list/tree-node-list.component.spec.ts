import { async, ComponentFixture, TestBed } from '@angular/core/testing';

import { TreeNodeListComponent } from './tree-node-list.component';

describe('TreeNodeListComponent', () => {
  let component: TreeNodeListComponent;
  let fixture: ComponentFixture<TreeNodeListComponent>;

  beforeEach(async(() => {
    TestBed.configureTestingModule({
      declarations: [ TreeNodeListComponent ]
    })
    .compileComponents();
  }));

  beforeEach(() => {
    fixture = TestBed.createComponent(TreeNodeListComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
